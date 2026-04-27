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

// Create a single instance of McpToolRegistry (populates internal ToolCatalog at startup)
var mcpToolRegistry = new McpToolRegistry();
RootsService? rootsService = null;
IHostApplicationLifetime? appLifetime = null;

// In-memory store for tool execution logs, exposed as MCP resources
var toolLogStore = new ToolLogStore();

// Load internal reasoning skills for guide sampling prompts (proprietary, not exposed)
var reasoningEngine = new GuideReasoningEngine();
reasoningEngine.LoadSkills();

// Load public skills for get_skill_details tool
var publicSkillLoader = new PublicSkillLoader();
publicSkillLoader.LoadIndex();

// Session-scoped active tool set — starts with always-on tools only
var activeToolSet = new ActiveToolSet();
var guideHandler = new GuideHandler(mcpToolRegistry.Catalog, activeToolSet, reasoningEngine);

// Register always-on tools (these are the only tools visible at session start)
RegisterAlwaysOnTools(activeToolSet, mcpToolRegistry, publicSkillLoader);

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
        options.ServerInstructions = @"TALXIS CLI MCP server for Power Platform / Dataverse development.

USE THE GUIDE TOOLS to discover operations for your task:
- guide: Cross-domain discovery — describe what you need
- guide_workspace: LOCAL development — scaffold components, understand workspace. Instant and reversible.
- guide_environment: LIVE environment — inspect/modify entities, attributes, relationships, layers, publishers. Requires profile. Takes 30s-5min.
- guide_deployment: Deployment lifecycle — import/export/pack solutions, manage components, publish. Requires profile.
- guide_data: LIVE data operations — SQL/FetchXML/OData queries, record CRUD, bulk ops, CMT migration. Requires profile.
- guide_config: CLI setup — auth credentials, connections, profiles, settings. Required before environment operations.

WORKFLOW: Call a guide tool → use execute_operation for immediate execution → discovered tools become direct calls on next turn.

PREFER LOCAL OPERATIONS: Workspace operations are instant and reversible. Environment operations take 30s-5min. Always prefer local workspace operations when developing — use environment operations only for inspection, troubleshooting, or deployment after local validation.";
        options.TaskStore = taskStore;
        options.SendTaskStatusNotifications = true;
        options.Capabilities = new ServerCapabilities
        {
            Tools = new ToolsCapability {},
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

// MCP tool listing — returns only active tools (always-on + dynamically injected)
ValueTask<ListToolsResult> ListToolsAsync(RequestContext<ListToolsRequestParams> ctx, CancellationToken ct)
    => ValueTask.FromResult(new ListToolsResult { Tools = activeToolSet.ListActiveTools() });

// MCP tool invocation — routes to guide handlers, execute_operation bridge, or direct CLI dispatch
async ValueTask<CallToolResult> CallToolAsync(RequestContext<CallToolRequestParams> ctx, CancellationToken ct)
{
    var p = ctx.Params;
    var toolName = p?.Name ?? string.Empty;
    if (string.IsNullOrEmpty(toolName))
        throw new McpException("Tool name is required.");

    // --- Route: Guide tools ---
    if (IsGuideTool(toolName))
    {
        return await HandleGuideToolAsync(toolName, p?.Arguments, ctx.Server, ct);
    }

    // --- Route: execute_operation bridge ---
    if (toolName == "execute_operation")
    {
        return await HandleExecuteOperationAsync(p?.Arguments, ctx, ct);
    }

    // --- Route: get_skill_details ---
    if (toolName == "get_skill_details")
    {
        return HandleGetSkillDetails(p?.Arguments);
    }

    // --- Route: MCP-specific tools (copilot-instructions) ---
    if (IsMcpSpecificTool(toolName))
    {
        var cmdType = mcpToolRegistry.FindCommandTypeByToolName(toolName);
        if (cmdType == null)
            throw new McpException($"Tool '{toolName}' not found.");
        return await ExecuteMcpSpecificToolWithCapturedOutputAsync(cmdType, p?.Arguments, ct);
    }

    // --- Route: Active injected tools (direct call) or any known tool ---
    return await DispatchCliToolAsync(toolName, p, ctx, ct);
}

// Handles guide and domain-specific guide tool calls
async ValueTask<CallToolResult> HandleGuideToolAsync(
    string guideName, IDictionary<string, JsonElement>? arguments, McpServer server, CancellationToken ct)
{
    var query = arguments?.TryGetValue("query", out var queryEl) == true ? queryEl.GetString() ?? "" : "";
    var top = arguments?.TryGetValue("top", out var topEl) == true ? (int)(topEl.GetDouble()) : 5;
    var workflow = arguments?.TryGetValue("workflow", out var wfEl) == true ? wfEl.GetString() : null;

    // Map domain guide name to workflow scope
    var workflowScope = guideName switch
    {
        "guide_workspace" => "local-development",
        "guide_environment" => null, // scoped guides handle both inspection and mutation
        "guide_deployment" => "deployment",
        "guide_data" => "data-operations",
        "guide_config" => "configuration",
        _ => null // generic guide
    };

    CallToolResult result;

    if (guideName == "guide_environment")
    {
        // Environment guide scopes to both inspection and mutation
        var inspectionEntries = mcpToolRegistry.Catalog.GetEntriesByWorkflow("environment-inspection");
        var mutationEntries = mcpToolRegistry.Catalog.GetEntriesByWorkflow("environment-mutation");
        var allEnvEntries = inspectionEntries.Concat(mutationEntries).ToList();

        if (string.IsNullOrEmpty(query))
        {
            // Return compact listing (no schemas) to avoid token bloat
            var tools = allEnvEntries.Select(McpToolRegistry.BuildToolDefinition).ToList();
            activeToolSet.InjectTools(tools);
            result = new CallToolResult
            {
                Content = [new TextContentBlock { Text = GuideHandler.BuildCompactListingResponse(allEnvEntries, "environment") }]
            };
        }
        else
        {
            // Scoped discovery across both environment workflows (run in parallel)
            var inspectionTask = guideHandler.HandleWorkflowGuideAsync(
                "environment-inspection", query, top, server, ct, guideName);
            var mutationTask = guideHandler.HandleWorkflowGuideAsync(
                "environment-mutation", query, top, server, ct, guideName);
            await Task.WhenAll(inspectionTask, mutationTask);

            var inspectionResult = inspectionTask.Result;
            var mutationResult = mutationTask.Result;

            // Merge text from both results
            var parts = new List<string>();
            var inspText = inspectionResult.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
            var mutText = mutationResult.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
            if (!string.IsNullOrEmpty(inspText)) parts.Add(inspText);
            if (!string.IsNullOrEmpty(mutText)) parts.Add(mutText);

            result = new CallToolResult
            {
                Content = [new TextContentBlock { Text = string.Join("\n\n", parts) }]
            };
        }
    }
    else if (workflowScope is not null)
    {
        result = await guideHandler.HandleWorkflowGuideAsync(workflowScope, query, top, server, ct, guideName);
    }
    else
    {
        result = await guideHandler.HandleAsync(query, workflow, top, server, ct);
    }

    // Clients discover injected tools by re-fetching tools/list on subsequent turns.

    return result;
}

// Bridge: execute_operation dispatches any tool from the internal catalog
async ValueTask<CallToolResult> HandleExecuteOperationAsync(
    IDictionary<string, JsonElement>? arguments, RequestContext<CallToolRequestParams> ctx, CancellationToken ct)
{
    if (arguments is null || !arguments.TryGetValue("operation", out var opEl))
        throw new McpException("'operation' parameter is required. Call a guide tool first to discover available operations.");

    var operationName = opEl.GetString() ?? throw new McpException("'operation' must be a non-empty string.");

    // Validate operation exists in internal catalog (not just active set — supports same-turn execution)
    var catalogEntry = mcpToolRegistry.Catalog.GetEntry(operationName);
    if (catalogEntry is null)
        throw new McpException($"Unknown operation '{operationName}'. Call a guide tool to discover available operations.");

    // MCP-specific in-process tools must be called directly, not through execute_operation
    if (IsMcpSpecificTool(operationName))
        throw new McpException($"'{operationName}' is an in-process tool — call it directly instead of through execute_operation.");

    // Parse arguments — accept either a JSON string or a JSON object
    Dictionary<string, JsonElement>? opArguments = null;
    if (arguments.TryGetValue("arguments", out var argsEl))
    {
        if (argsEl.ValueKind == JsonValueKind.String)
        {
            var argsStr = argsEl.GetString();
            if (!string.IsNullOrEmpty(argsStr))
            {
                try { opArguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argsStr); }
                catch (JsonException ex) { throw new McpException($"Invalid JSON in 'arguments' parameter: {ex.Message}"); }
            }
        }
        else if (argsEl.ValueKind == JsonValueKind.Object)
        {
            opArguments = new Dictionary<string, JsonElement>();
            foreach (var prop in argsEl.EnumerateObject())
                opArguments[prop.Name] = prop.Value;
        }
    }

    // Dispatch through the normal CLI tool pipeline
    var descriptor = catalogEntry.Descriptor;

    // Check for task-augmented execution — pass parsed inner arguments, not the outer execute_operation params
    if (ctx.Params?.Task is { } taskMetadata && descriptor.SupportsTaskExecution)
    {
        return await ExecuteAsTaskAsync(ctx, operationName, taskMetadata, ct, overrideArguments: opArguments);
    }

    return await ExecuteCliToolAsync(operationName, opArguments, ctx, ct);
}

// Dispatches a CLI tool (active injected tool or fallback to internal catalog)
async ValueTask<CallToolResult> DispatchCliToolAsync(
    string toolName, CallToolRequestParams? p, RequestContext<CallToolRequestParams> ctx, CancellationToken ct)
{
    // Check if it's an active tool (injected via guide) or in the internal catalog
    var cmdType = mcpToolRegistry.FindCommandTypeByToolName(toolName);
    if (cmdType == null)
        throw new McpException($"Tool '{toolName}' not found. Use a guide tool to discover available operations, then call execute_operation or wait for the tool to appear in your tool list.");

    var descriptor = mcpToolRegistry.GetDescriptor(toolName);
    if (p?.Task is { } taskMetadata && descriptor?.SupportsTaskExecution == true)
    {
        return await ExecuteAsTaskAsync(ctx, toolName, taskMetadata, ct);
    }

    IReadOnlyDictionary<string, JsonElement>? cliArguments = p?.Arguments is null
        ? null
        : new Dictionary<string, JsonElement>(p.Arguments);

    return await ExecuteCliToolAsync(toolName, cliArguments, ctx, ct);
}

// Shared CLI tool execution — used by both execute_operation and direct dispatch
async Task<CallToolResult> ExecuteCliToolAsync(
    string toolName, IReadOnlyDictionary<string, JsonElement>? cliArguments,
    RequestContext<CallToolRequestParams> ctx, CancellationToken ct)
{
    try
    {
        var cliCommandAdapter = new CliCommandAdapter();
        var cliArgs = cliCommandAdapter.BuildCliArgs(toolName, cliArguments);

        var mcpLoggerProvider = ctx.Server.AsClientLoggerProvider();
        var mcpLogger = mcpLoggerProvider.CreateLogger($"txc.{toolName}");
        var progressToken = ctx.Params?.ProgressToken;
        var logForwarder = new McpLogForwarder(mcpLogger, ctx.Server, progressToken);

        mcpLogger.LogInformation("Starting tool: {ToolName}", toolName);

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
// overrideArguments: when called from execute_operation, these are the parsed inner tool arguments
// (not the outer execute_operation params). Null for direct tool calls.
async ValueTask<CallToolResult> ExecuteAsTaskAsync(
    RequestContext<CallToolRequestParams> ctx,
    string toolName,
    McpTaskMetadata taskMetadata,
    CancellationToken ct,
    IReadOnlyDictionary<string, System.Text.Json.JsonElement>? overrideArguments = null)
{
    // Extract all needed values from ctx/p BEFORE Task.Run — request context objects
    // may not be safe to access after the handler returns.
    var server = ctx.Server;
    var sessionId = server.SessionId;
    // Use override arguments (from execute_operation) if provided, otherwise extract from ctx
    var arguments = overrideArguments is not null
        ? new Dictionary<string, System.Text.Json.JsonElement>(overrideArguments)
        : ctx.Params?.Arguments is null
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

// Helper: checks if a tool name is a guide tool
bool IsGuideTool(string toolName)
{
    return toolName is "guide" or "guide_workspace" or "guide_environment"
        or "guide_deployment" or "guide_data" or "guide_config";
}

// Helper: checks if a tool is an MCP-specific in-process tool (not a CLI subprocess)
bool IsMcpSpecificTool(string toolName)
{
    return toolName is "copilot-instructions";
}

// Handler: get_skill_details — returns public skill content from embedded resources
CallToolResult HandleGetSkillDetails(IDictionary<string, JsonElement>? arguments)
{
    if (arguments is null || !arguments.TryGetValue("skill_id", out var skillIdEl))
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = $"'skill_id' parameter is required.\n\n{publicSkillLoader.GetSkillsIndexPrompt()}" }],
            IsError = true
        };

    var skillId = skillIdEl.GetString() ?? "";
    var content = publicSkillLoader.GetSkillContent(skillId);

    if (content is null)
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = $"Skill '{skillId}' not found.\n\n{publicSkillLoader.GetSkillsIndexPrompt()}" }],
            IsError = true
        };

    return new CallToolResult
    {
        Content = [new TextContentBlock { Text = content }]
    };
}

// Registers the always-on tools in the ActiveToolSet
void RegisterAlwaysOnTools(ActiveToolSet toolSet, McpToolRegistry registry, PublicSkillLoader skillLoader)
{
    // Generic guide — cross-domain discovery
    toolSet.AddAlwaysOn(new Tool
    {
        Name = "guide",
        Description = @"Discovers tools for your task. Describe what you want to do and this tool will find the right operations.

USE THIS WHEN:
- You need to find which tool to use for a task
- You're unsure whether to use local workspace or live environment tools

WORKFLOWS: local-development, environment-inspection, environment-mutation, data-operations, deployment, configuration, changeset-management

After calling guide, you can IMMEDIATELY use execute_operation with the returned tool name and arguments. On your next turn, the tools will also be available as direct calls.",
        InputSchema = BuildGuideInputSchema()
    });

    // Domain guides — each advertises a specific capability area
    toolSet.AddAlwaysOn(new Tool
    {
        Name = "guide_workspace",
        Description = @"Helps you build Power Platform apps LOCALLY. Scaffold Dataverse components (tables, forms, views, plugins), understand workspace structure, convert data models, pack solutions. LOCAL operations are instant and reversible — always prefer this over environment operations when developing.",
        InputSchema = BuildGuideInputSchema()
    });

    toolSet.AddAlwaysOn(new Tool
    {
        Name = "guide_environment",
        Description = @"Inspects and modifies a LIVE Dataverse environment. List/describe/create/update/delete entities, attributes, relationships, option sets. Inspect solution layers, component dependencies. Manage publishers. Requires an active profile. Environment operations take 30s-5min — use workspace operations for development, this for inspection and troubleshooting.",
        InputSchema = BuildGuideInputSchema()
    });

    toolSet.AddAlwaysOn(new Tool
    {
        Name = "guide_deployment",
        Description = @"Manages the deployment lifecycle. Import/export/pack solutions, manage solution components, publish customizations, check uninstall safety, import packages. Handles the full flow: local build → pack → import → publish → verify. Requires an active profile.",
        InputSchema = BuildGuideInputSchema()
    });

    toolSet.AddAlwaysOn(new Tool
    {
        Name = "guide_data",
        Description = @"Queries and manages LIVE Dataverse data. Execute SQL, FetchXML, or OData queries. CRUD operations on records. Bulk create/update/upsert. File upload/download. Associate/disassociate records. Export/import CMT data packages for migration. Requires an active profile.",
        InputSchema = BuildGuideInputSchema()
    });

    toolSet.AddAlwaysOn(new Tool
    {
        Name = "guide_config",
        Description = @"Sets up and manages CLI configuration. Create auth credentials (service principals), connections, and profiles. Pin profiles to workspaces. Manage CLI settings. Validate profile connectivity. Required before any environment operation.",
        InputSchema = BuildGuideInputSchema()
    });

    // execute_operation bridge — for same-turn execution
    toolSet.AddAlwaysOn(new Tool
    {
        Name = "execute_operation",
        Description = @"Execute any operation discovered via a guide tool. Use this for IMMEDIATE execution in the same turn.

IMPORTANT: Call 'guide' first to discover available operations and their parameters.
If a tool is marked DESTRUCTIVE, ask the user for confirmation before executing.",
        InputSchema = BuildExecuteOperationInputSchema(),
        Annotations = new ToolAnnotations { DestructiveHint = true }
    });

    // get_skill_details — public knowledge base
    var skillsDescription = @"Fetches detailed Power Platform development guidance.

" + skillLoader.GetSkillsIndexPrompt() + @"

For team-specific naming conventions and coding standards, use native Agent Skills (SKILL.md files) in your repository.";

    toolSet.AddAlwaysOn(new Tool
    {
        Name = "get_skill_details",
        Description = skillsDescription,
        InputSchema = BuildGetSkillDetailsInputSchema(),
        Annotations = new ToolAnnotations { ReadOnlyHint = true }
    });

    // copilot-instructions — existing MCP-specific tool (registered via catalog too, but needs always-on)
    var copilotEntry = registry.Catalog.GetEntry("copilot-instructions");
    if (copilotEntry is not null)
    {
        toolSet.AddAlwaysOn(McpToolRegistry.BuildToolDefinition(copilotEntry));
    }
}

// Builds the JSON Schema for guide tool input parameters
JsonElement BuildGuideInputSchema()
{
    var schema = new Dictionary<string, object?>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["query"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Natural language description of what you want to do"
            },
            ["top"] = new Dictionary<string, object?>
            {
                ["type"] = "integer",
                ["description"] = "Number of results to return (default 5)"
            },
            ["workflow"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Explicit workflow filter: local-development, environment-inspection, environment-mutation, data-operations, deployment, configuration, changeset-management"
            }
        },
        ["required"] = new List<string> { "query" }
    };
    return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(schema));
}

// Builds the JSON Schema for execute_operation input parameters
JsonElement BuildExecuteOperationInputSchema()
{
    var schema = new Dictionary<string, object?>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["operation"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Tool name returned by guide (e.g. 'environment_solution_import')"
            },
            ["arguments"] = new Dictionary<string, object?>
            {
                ["description"] = "Arguments matching the tool's schema. Pass as a JSON object or a JSON-encoded string."
            }
        },
        ["required"] = new List<string> { "operation" }
    };
    return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(schema));
}

// Builds the JSON Schema for get_skill_details input parameters
JsonElement BuildGetSkillDetailsInputSchema()
{
    var schema = new Dictionary<string, object?>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["skill_id"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "ID of the skill to fetch (e.g. 'component-creation', 'deployment-workflow')"
            }
        },
        ["required"] = new List<string> { "skill_id" }
    };
    return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(schema));
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
        // Failure: prefer stdout (contains structured error envelope from
        // OutputFormatter.WriteResult), fall back to stderr error lines.
        var errorText = !string.IsNullOrWhiteSpace(result.Output)
            ? result.Output
            : !string.IsNullOrWhiteSpace(result.LastErrors)
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
