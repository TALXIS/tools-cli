#pragma warning disable MCPEXP001

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Reflection;
using TALXIS.CLI.MCP;
using TALXIS.CLI.Logging;

// Create a single instance of McpToolRegistry
var mcpToolRegistry = new McpToolRegistry();
RootsService? rootsService = null;
IHostApplicationLifetime? appLifetime = null;

// In-memory store for tool execution logs, exposed as MCP resources
var toolLogStore = new ToolLogStore();

// Task store with cancel propagation for long-running operations (experimental MCP feature)
var taskStore = new CancellableTaskStore(new InMemoryMcpTaskStore(
    defaultTtl: TimeSpan.FromHours(4),
    pollInterval: TimeSpan.FromSeconds(2)
));

try
{
    var builder = new HostApplicationBuilder(args);
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(consoleLogOptions =>
    {
        // Configure all logs to go to stderr
        consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
    });

builder.Services
    .AddMcpServer(options =>
    {
        var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
        options.ServerInfo = new Implementation
        {
            Name = "TALXIS CLI MCP (txc-mcp)",
            Version = version
        };
        options.ServerInstructions = "This server is a wrapper for the TALXIS CLI. It provides tools for developers to implement functionality in their repository.";
        options.TaskStore = taskStore;
        options.SendTaskStatusNotifications = true;
        options.Capabilities = new ServerCapabilities
        {
            Tools = new ToolsCapability
            {
                ListChanged = true
            },
            Logging = new LoggingCapability {},
            Resources = new ResourcesCapability {}
        };
    })
    .WithListToolsHandler(ListToolsAsync)
    .WithCallToolHandler(CallToolAsync)
    .WithListResourcesHandler(ListResourcesAsync)
    .WithReadResourceHandler(ReadResourceAsync)
    .WithStdioServerTransport();

    var host = builder.Build();
    // Initialize RootsService with the McpServer instance from DI
    var mcpServer = host.Services.GetRequiredService<McpServer>();
    var loggerFactory = host.Services.GetService<ILoggerFactory>();
    rootsService = new RootsService(mcpServer, loggerFactory?.CreateLogger<RootsService>());
    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
    appLifetime = lifetime;
    await host.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Fatal error starting MCP server: {LogRedactionFilter.Redact(ex.ToString())}");
    return 1;
}

// MCP tool listing
ValueTask<ListToolsResult> ListToolsAsync(RequestContext<ListToolsRequestParams> ctx, CancellationToken ct)
    => ValueTask.FromResult(new ListToolsResult { Tools = mcpToolRegistry.ListTools() });

// MCP tool invocation
async ValueTask<CallToolResult> CallToolAsync(RequestContext<CallToolRequestParams> ctx, CancellationToken ct)
{
    var p = ctx.Params;
    var toolName = p?.Name ?? string.Empty;
    if (string.IsNullOrEmpty(toolName))
        throw new McpException("Tool name is required.");
    var cmdType = mcpToolRegistry.FindCommandTypeByToolName(toolName);
    if (cmdType == null)
        throw new McpException($"Tool '{toolName}' not found.");
    
    // Check if client requested task-augmented execution
    var descriptor = mcpToolRegistry.GetDescriptor(toolName);
    if (p?.Task is { } taskMetadata && descriptor?.SupportsTaskExecution == true)
    {
        return await ExecuteAsTaskAsync(ctx, toolName, taskMetadata, ct);
    }

    try
    {
        // Check if this is an MCP-specific tool (not part of the main CLI hierarchy)
        if (IsMcpSpecificTool(toolName))
        {
            return await ExecuteMcpSpecificToolWithCapturedOutputAsync(cmdType, p?.Arguments, ct);
        }

        var cliCommandAdapter = new CliCommandAdapter();
        IReadOnlyDictionary<string, System.Text.Json.JsonElement>? cliArguments = p?.Arguments is null
            ? null
            : new Dictionary<string, System.Text.Json.JsonElement>(p.Arguments);
        var cliArgs = cliCommandAdapter.BuildCliArgs(toolName, cliArguments);

        // Create a per-request log forwarder that streams subprocess logs to the MCP client
        var mcpLoggerProvider = ctx.Server.AsClientLoggerProvider();
        var mcpLogger = mcpLoggerProvider.CreateLogger($"txc.{toolName}");
        var progressToken = ctx.Params?.ProgressToken;
        var logForwarder = new McpLogForwarder(mcpLogger, ctx.Server, progressToken);

        mcpLogger.LogInformation("Starting tool: {ToolName}", toolName);

        // Resolve working directory from client roots (lazy, cached)
        string? workingDirectory = rootsService is not null
            ? await rootsService.GetWorkingDirectoryAsync(ct)
            : null;

        CliSubprocessResult result = await CliSubprocessRunner.RunAsync(cliArgs, logForwarder, ct, workingDirectory);

        mcpLogger.LogInformation("Tool completed: {ToolName} (exit code {ExitCode})", toolName, result.ExitCode);

        return BuildToolResult(toolName, result);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception ex)
    {
        return new CallToolResult { Content = [new TextContentBlock { Text = LogRedactionFilter.Redact(ex.ToString()) }], IsError = true };
    }

}

// Execute a tool call as an MCP task (call-now, fetch-later pattern)
async ValueTask<CallToolResult> ExecuteAsTaskAsync(
    RequestContext<CallToolRequestParams> ctx,
    string toolName,
    McpTaskMetadata taskMetadata,
    CancellationToken ct)
{
    // Extract all needed values from ctx/p BEFORE Task.Run — request context objects
    // may not be safe to access after the handler returns.
    var server = ctx.Server;
    var sessionId = server.SessionId;
    var arguments = ctx.Params?.Arguments is null
        ? null
        : new Dictionary<string, System.Text.Json.JsonElement>(ctx.Params.Arguments);
    var progressToken = ctx.Params?.ProgressToken;
    var requestId = ctx.JsonRpcRequest.Id;
    var jsonRpcRequest = ctx.JsonRpcRequest;

    // Create the task in the store
    var mcpTask = await taskStore.CreateTaskAsync(
        taskMetadata,
        requestId,
        jsonRpcRequest,
        sessionId,
        ct);

    // Link to app lifetime so shutdown cancels running tasks (prevents orphaned subprocesses)
    var stoppingToken = appLifetime?.ApplicationStopping ?? CancellationToken.None;
    var taskCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
    taskStore.RegisterCancellationToken(mcpTask.TaskId, taskCts);

    // Fire-and-forget: run the tool in the background
    // All captured variables are locals — no ctx/p references inside the closure.
    _ = Task.Run(async () =>
    {
        try
        {
            // Mark task as working
            var workingTask = await taskStore.UpdateTaskStatusAsync(
                mcpTask.TaskId, McpTaskStatus.Working, null, sessionId, CancellationToken.None);
            await server.NotifyTaskStatusAsync(workingTask, CancellationToken.None);

            // Build CLI args and run subprocess
            var cliCommandAdapter = new CliCommandAdapter();
            IReadOnlyDictionary<string, System.Text.Json.JsonElement>? cliArguments = arguments;
            var cliArgs = cliCommandAdapter.BuildCliArgs(toolName, cliArguments);

            var mcpLoggerProvider = server.AsClientLoggerProvider();
            var mcpLogger = mcpLoggerProvider.CreateLogger($"txc.{toolName}");
            var logForwarder = new McpLogForwarder(mcpLogger, server, progressToken);

            mcpLogger.LogInformation("Starting task-augmented tool: {ToolName} (taskId: {TaskId})", toolName, mcpTask.TaskId);

            string? workingDirectory = rootsService is not null
                ? await rootsService.GetWorkingDirectoryAsync(taskCts.Token)
                : null;

            CliSubprocessResult result = await CliSubprocessRunner.RunAsync(cliArgs, logForwarder, taskCts.Token, workingDirectory);

            mcpLogger.LogInformation("Task completed: {ToolName} (exit code {ExitCode}, taskId: {TaskId})", toolName, result.ExitCode, mcpTask.TaskId);

            var callToolResult = BuildToolResult(toolName, result);

            var finalStatus = result.ExitCode != 0 ? McpTaskStatus.Failed : McpTaskStatus.Completed;
            var resultElement = System.Text.Json.JsonSerializer.SerializeToElement(callToolResult);
            var finalTask = await taskStore.StoreTaskResultAsync(
                mcpTask.TaskId, finalStatus, resultElement, sessionId, CancellationToken.None);
            await server.NotifyTaskStatusAsync(finalTask, CancellationToken.None);
        }
        catch (OperationCanceledException) when (taskCts.Token.IsCancellationRequested)
        {
            // Task was cancelled via tasks/cancel — update status
            try
            {
                var cancelledTask = await taskStore.UpdateTaskStatusAsync(
                    mcpTask.TaskId, McpTaskStatus.Cancelled, "Cancelled by client", sessionId, CancellationToken.None);
                await server.NotifyTaskStatusAsync(cancelledTask, CancellationToken.None);
            }
            catch { /* Best effort */ }
        }
        catch (Exception ex)
        {
            try
            {
                var errorResult = new CallToolResult
                {
                    IsError = true,
                    Content = [new TextContentBlock { Text = $"Task execution failed: {LogRedactionFilter.Redact(ex.ToString())}" }]
                };
                var errorElement = System.Text.Json.JsonSerializer.SerializeToElement(errorResult);
                var failedTask = await taskStore.StoreTaskResultAsync(
                    mcpTask.TaskId, McpTaskStatus.Failed, errorElement, sessionId, CancellationToken.None);
                await server.NotifyTaskStatusAsync(failedTask, CancellationToken.None);
            }
            catch { /* Best effort */ }
        }
        finally
        {
            taskStore.UnregisterCancellationToken(mcpTask.TaskId);
            taskCts.Dispose();
        }
    }, CancellationToken.None);

    // Return immediately with task handle
    return new CallToolResult { Task = mcpTask };
}

// Helper method to check if a tool is MCP-specific
bool IsMcpSpecificTool(string toolName)
{
    // For now, we'll hardcode the list of MCP-specific tools
    // This could be made more dynamic in the future
    return toolName == "copilot-instructions";
}

// Helper method to execute MCP-specific tools directly
async Task<int> ExecuteMcpSpecificToolAsync(Type commandType, IDictionary<string, System.Text.Json.JsonElement>? arguments, CancellationToken ct)
{
    try
    {
        var command = Activator.CreateInstance(commandType);
        if (command == null)
            throw new InvalidOperationException($"Could not create instance of {commandType.FullName}");

        // Set properties from arguments
        if (arguments != null)
        {
            foreach (var arg in arguments)
            {
                var prop = commandType.GetProperty(arg.Key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop != null && prop.CanWrite)
                {
                    var value = ConvertJsonElementToPropertyType(arg.Value, prop.PropertyType);
                    prop.SetValue(command, value);
                }
            }
        }

        // Execute the command
        var runMethod = commandType.GetMethod("RunAsync");
        if (runMethod != null)
        {
            // For RunAsync methods, pass null for CliContext since MCP-specific tools don't need it
            var result = runMethod.Invoke(command, new object?[] { null });
            if (result is Task<int> taskResult)
            {
                return await taskResult.WaitAsync(ct);
            }
            else if (result is Task task)
            {
                await task.WaitAsync(ct);
                return 0;
            }
        }

        // Fallback to synchronous Run method
        var syncRunMethod = commandType.GetMethod("Run");
        if (syncRunMethod != null)
        {
            // For Run methods, pass null for CliContext since MCP-specific tools don't need it
            syncRunMethod.Invoke(command, new object?[] { null });
            return 0;
        }

        throw new InvalidOperationException($"No suitable Run or RunAsync method found on {commandType.FullName}");
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error executing MCP-specific tool: {LogRedactionFilter.Redact(ex.Message)}");
        return 1;
    }
}

async Task<CallToolResult> ExecuteMcpSpecificToolWithCapturedOutputAsync(Type commandType, IDictionary<string, System.Text.Json.JsonElement>? arguments, CancellationToken ct)
{
    var output = new StringWriter();

    // Redirect OutputWriter (result data) to our capture buffer.
    // In-process MCP tools use OutputWriter.WriteLine() for result data.
    using var redirect = TALXIS.CLI.Core.OutputWriter.RedirectTo(output);

    try
    {
        var exitCode = await ExecuteMcpSpecificToolAsync(commandType, arguments, ct);
        output.Flush();

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = output.ToString() }],
            IsError = exitCode != 0
        };
    }
    catch (Exception ex)
    {
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = LogRedactionFilter.Redact(ex.ToString()) }],
            IsError = true
        };
    }
}

// Build a CallToolResult, including a resource_link to the full log on failure
CallToolResult BuildToolResult(string toolName, CliSubprocessResult result)
{
    var content = new List<ContentBlock>();

    if (result.ExitCode == 0)
    {
        // Success: return stdout content
        content.Add(new TextContentBlock { Text = result.Output });
    }
    else
    {
        // Failure: include error summary as text
        var errorText = !string.IsNullOrWhiteSpace(result.LastErrors)
            ? result.LastErrors
            : $"Tool '{toolName}' failed with exit code {result.ExitCode}.";
        content.Add(new TextContentBlock { Text = errorText });

        // Store the full execution log and add a resource_link so the client can fetch details
        if (!string.IsNullOrWhiteSpace(result.FullLog))
        {
            var logUri = toolLogStore.Store(toolName, result.FullLog, result.LastErrors, isError: true);
            content.Add(new ResourceLinkBlock
            {
                Uri = logUri,
                Name = $"Full execution log for {toolName}",
                Description = "Complete stderr log from the subprocess. Use resources/read to retrieve.",
                MimeType = "text/plain"
            });
        }
    }

    return new CallToolResult
    {
        Content = content,
        IsError = result.ExitCode != 0
    };
}

// MCP resource listing — exposes stored tool execution logs
ValueTask<ListResourcesResult> ListResourcesAsync(RequestContext<ListResourcesRequestParams> ctx, CancellationToken ct)
{
    var entries = toolLogStore.ListAll();
    var resources = entries.Select(e => new Resource
    {
        Uri = e.Uri,
        Name = $"Execution log: {e.Entry.ToolName}",
        Description = $"Full stderr log from {e.Entry.ToolName} at {e.Entry.Timestamp:u}",
        MimeType = "text/plain"
    }).ToList();

    return ValueTask.FromResult(new ListResourcesResult { Resources = resources });
}

// MCP resource read — returns the full execution log for a given URI
ValueTask<ReadResourceResult> ReadResourceAsync(RequestContext<ReadResourceRequestParams> ctx, CancellationToken ct)
{
    var uri = ctx.Params?.Uri ?? throw new McpException("Resource URI is required.");

    if (!toolLogStore.TryGet(uri, out var entry) || entry is null)
        throw new McpException($"Resource not found: {uri}");

    return ValueTask.FromResult(new ReadResourceResult
    {
        Contents = [new TextResourceContents { Uri = uri, MimeType = "text/plain", Text = entry.FullLog }]
    });
}

// Helper method to convert JsonElement to the target property type
object? ConvertJsonElementToPropertyType(System.Text.Json.JsonElement jsonElement, Type targetType)
{
    if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.Null)
        return null;

    if (targetType == typeof(string))
        return jsonElement.GetString();
    
    if (targetType == typeof(int) || targetType == typeof(int?))
        return jsonElement.GetInt32();
    
    if (targetType == typeof(bool) || targetType == typeof(bool?))
        return jsonElement.GetBoolean();
    
    if (targetType == typeof(double) || targetType == typeof(double?))
        return jsonElement.GetDouble();
    
    // For more complex types, try deserializing from JSON
    return System.Text.Json.JsonSerializer.Deserialize(jsonElement.GetRawText(), targetType);
}
