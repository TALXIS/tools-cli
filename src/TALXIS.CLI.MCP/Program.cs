using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TALXIS.CLI.MCP;
using System.Reflection;

// Create a single instance of McpToolRegistry
var mcpToolRegistry = new McpToolRegistry();

try
{
    var builder = new HostApplicationBuilder(args);
    builder.Logging.AddConsole(consoleLogOptions =>
    {
        // Configure all logs to go to stderr
        consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
    });

builder.Services.AddMcpServer(options =>
{
    var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
    options.ServerInfo = new Implementation
    {
        Name = "TALXIS CLI MCP (txc-mcp)",
        Version = version
    };
    options.ServerInstructions = "This server is a wrapper for the TALXIS CLI. It provides tools for developers to implement functionality in their repository.";
    options.Capabilities = new ServerCapabilities
    {
        Tools = new ToolsCapability
        {
            ListChanged = true,
            ListToolsHandler = ListToolsAsync,
            CallToolHandler = CallToolAsync
        }
    };
    })
    .WithStdioServerTransport();

    await builder.Build().RunAsync();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Fatal error starting MCP server: {ex}");
    Environment.Exit(1);
}// MCP tool listing
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

    var output = new StringWriter();
    var origOut = Console.Out;
    var origErr = Console.Error;
    Console.SetOut(output);
    Console.SetError(output);
    
    try
    {
        // Check if this is an MCP-specific tool (not part of the main CLI hierarchy)
        if (IsMcpSpecificTool(toolName))
        {
            // Execute MCP-specific tools directly
            var exitCode = await ExecuteMcpSpecificToolAsync(cmdType, p?.Arguments, ct);
            Console.Out.Flush();
            Console.Error.Flush();
            if (exitCode != 0)
            {
                return new CallToolResult {
                    Content = [new TextContentBlock { Text = output.ToString(), Type = "text" }],
                    IsError = true
                };
            }
        }
        else
        {
            // Execute regular CLI commands through the main CLI
            var cliCommandAdapter = new CliCommandAdapter();
            var cliArgs = cliCommandAdapter.BuildCliArgs(toolName, p?.Arguments);
            var exitCode = await TALXIS.CLI.Program.RunCli(cliArgs.ToArray());
            Console.Out.Flush();
            Console.Error.Flush();
            if (exitCode != 0)
            {
                return new CallToolResult {
                    Content = [new TextContentBlock { Text = output.ToString(), Type = "text" }],
                    IsError = true
                };
            }
        }
    }
    catch (Exception ex)
    {
        return new CallToolResult { Content = [new TextContentBlock { Text = ex.ToString(), Type = "text" }], IsError = true };
    }
    finally
    {
        Console.SetOut(origOut);
        Console.SetError(origErr);
    }
    return new CallToolResult { Content = [new TextContentBlock { Text = output.ToString(), Type = "text" }], IsError = false };
}

// Helper method to check if a tool is MCP-specific
bool IsMcpSpecificTool(string toolName)
{
    // For now, we'll hardcode the list of MCP-specific tools
    // This could be made more dynamic in the future
    return toolName == "copilot-instructions";
}

// Helper method to execute MCP-specific tools directly
async Task<int> ExecuteMcpSpecificToolAsync(Type commandType, IReadOnlyDictionary<string, System.Text.Json.JsonElement>? arguments, CancellationToken ct)
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
                return await taskResult;
            }
            else if (result is Task task)
            {
                await task;
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
    catch (Exception ex)
    {
        Console.WriteLine($"Error executing MCP-specific tool: {ex.Message}");
        return 1;
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
