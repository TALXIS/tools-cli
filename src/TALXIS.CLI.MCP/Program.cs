#pragma warning disable MCPEXP001

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Reflection;
using TALXIS.CLI.MCP;

// Create a single instance of McpToolRegistry
var mcpToolRegistry = new McpToolRegistry();
RootsService? rootsService = null;

// Per-task cancellation tracking for tasks/cancel support
var taskCancellationTokens = new ConcurrentDictionary<string, CancellationTokenSource>();

// Task store for long-running operations (experimental MCP feature)
var taskStore = new InMemoryMcpTaskStore(
    defaultTtl: TimeSpan.FromHours(4),
    pollInterval: TimeSpan.FromSeconds(2)
);

try
{
    var builder = new HostApplicationBuilder(args);
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
            Logging = new LoggingCapability {}
        };
    })
    .WithListToolsHandler(ListToolsAsync)
    .WithCallToolHandler(CallToolAsync)
    .WithStdioServerTransport();

    var host = builder.Build();
    // Initialize RootsService with the McpServer instance from DI
    var mcpServer = host.Services.GetRequiredService<McpServer>();
    var loggerFactory = host.Services.GetService<ILoggerFactory>();
    rootsService = new RootsService(mcpServer, loggerFactory?.CreateLogger<RootsService>());
    await host.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Fatal error starting MCP server: {ex}");
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

        // When the tool fails and stdout is empty, include error log messages in the result
        // so the client can display them (log notifications may not be visible in all clients)
        var resultText = result.Output;
        if (result.ExitCode != 0 && string.IsNullOrWhiteSpace(resultText) && !string.IsNullOrWhiteSpace(result.LastErrors))
        {
            resultText = result.LastErrors;
        }

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = resultText }],
            IsError = result.ExitCode != 0
        };
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception ex)
    {
        return new CallToolResult { Content = [new TextContentBlock { Text = ex.ToString() }], IsError = true };
    }

}

// Execute a tool call as an MCP task (call-now, fetch-later pattern)
async ValueTask<CallToolResult> ExecuteAsTaskAsync(
    RequestContext<CallToolRequestParams> ctx,
    string toolName,
    McpTaskMetadata taskMetadata,
    CancellationToken ct)
{
    var p = ctx.Params;
    var server = ctx.Server;

    // Create the task in the store
    var mcpTask = await taskStore.CreateTaskAsync(
        taskMetadata,
        ctx.JsonRpcRequest.Id,
        ctx.JsonRpcRequest,
        server.SessionId,
        ct);

    // Create a per-task CTS so tasks/cancel can stop the subprocess
    var taskCts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
    taskCancellationTokens[mcpTask.TaskId] = taskCts;

    // Fire-and-forget: run the tool in the background
    _ = Task.Run(async () =>
    {
        try
        {
            // Mark task as working
            var workingTask = await taskStore.UpdateTaskStatusAsync(
                mcpTask.TaskId, McpTaskStatus.Working, null, server.SessionId, CancellationToken.None);
            await server.NotifyTaskStatusAsync(workingTask, CancellationToken.None);

            // Build CLI args and run subprocess
            var cliCommandAdapter = new CliCommandAdapter();
            IReadOnlyDictionary<string, System.Text.Json.JsonElement>? cliArguments = p?.Arguments is null
                ? null
                : new Dictionary<string, System.Text.Json.JsonElement>(p.Arguments);
            var cliArgs = cliCommandAdapter.BuildCliArgs(toolName, cliArguments);

            var mcpLoggerProvider = server.AsClientLoggerProvider();
            var mcpLogger = mcpLoggerProvider.CreateLogger($"txc.{toolName}");
            var logForwarder = new McpLogForwarder(mcpLogger, server, ctx.Params?.ProgressToken);

            mcpLogger.LogInformation("Starting task-augmented tool: {ToolName} (taskId: {TaskId})", toolName, mcpTask.TaskId);

            string? workingDirectory = rootsService is not null
                ? await rootsService.GetWorkingDirectoryAsync(taskCts.Token)
                : null;

            CliSubprocessResult result = await CliSubprocessRunner.RunAsync(cliArgs, logForwarder, taskCts.Token, workingDirectory);

            mcpLogger.LogInformation("Task completed: {ToolName} (exit code {ExitCode}, taskId: {TaskId})", toolName, result.ExitCode, mcpTask.TaskId);

            var taskResultText = result.Output;
            if (result.ExitCode != 0 && string.IsNullOrWhiteSpace(taskResultText) && !string.IsNullOrWhiteSpace(result.LastErrors))
            {
                taskResultText = result.LastErrors;
            }

            var callToolResult = new CallToolResult
            {
                Content = [new TextContentBlock { Text = taskResultText }],
                IsError = result.ExitCode != 0
            };

            var finalStatus = result.ExitCode != 0 ? McpTaskStatus.Failed : McpTaskStatus.Completed;
            var resultElement = System.Text.Json.JsonSerializer.SerializeToElement(callToolResult);
            var finalTask = await taskStore.StoreTaskResultAsync(
                mcpTask.TaskId, finalStatus, resultElement, server.SessionId, CancellationToken.None);
            await server.NotifyTaskStatusAsync(finalTask, CancellationToken.None);
        }
        catch (OperationCanceledException) when (taskCts.Token.IsCancellationRequested)
        {
            // Task was cancelled — store handles status via tasks/cancel handler
        }
        catch (Exception ex)
        {
            try
            {
                var errorResult = new CallToolResult
                {
                    IsError = true,
                    Content = [new TextContentBlock { Text = $"Task execution failed: {ex.Message}" }]
                };
                var errorElement = System.Text.Json.JsonSerializer.SerializeToElement(errorResult);
                var failedTask = await taskStore.StoreTaskResultAsync(
                    mcpTask.TaskId, McpTaskStatus.Failed, errorElement, server.SessionId, CancellationToken.None);
                await server.NotifyTaskStatusAsync(failedTask, CancellationToken.None);
            }
            catch { /* Best effort */ }
        }
        finally
        {
            taskCancellationTokens.TryRemove(mcpTask.TaskId, out _);
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
        Console.Error.WriteLine($"Error executing MCP-specific tool: {ex.Message}");
        return 1;
    }
}

async Task<CallToolResult> ExecuteMcpSpecificToolWithCapturedOutputAsync(Type commandType, IDictionary<string, System.Text.Json.JsonElement>? arguments, CancellationToken ct)
{
    var output = new StringWriter();

    // Redirect OutputWriter (result data) to our capture buffer.
    // In-process MCP tools use OutputWriter.WriteLine() for result data.
    using var redirect = TALXIS.CLI.Shared.OutputWriter.RedirectTo(output);

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
            Content = [new TextContentBlock { Text = ex.ToString() }],
            IsError = true
        };
    }
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
