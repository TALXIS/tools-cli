using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Reflection;
using System.Text.Json;
using TALXIS.CLI;
using TALXIS.CLI.MCP;

var builder = new HostApplicationBuilder(args);
builder.Logging.AddConsole(consoleLogOptions =>
{
    // Configure all logs to go to stderr
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

// Create a single instance of McpToolRegistry
var mcpToolRegistry = new McpToolRegistry();

builder.Services.AddMcpServer(options =>
{
    var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
    options.ServerInfo = new Implementation
    {
        Name = "TALXIS CLI MCP",
        Version = version
    };
    options.ServerInstructions = "This server is a wrapper for the TALXIS CLI. It allows MCP clients to execute the same commands through this server as if they were running the CLI in their terminal.";
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

    var cliCommandAdapter = new CliCommandAdapter();
    var cliArgs = cliCommandAdapter.BuildCliArgs(toolName, p?.Arguments);

    var output = new StringWriter();
    var origOut = Console.Out;
    Console.SetOut(output);
    try
    {
        await TALXIS.CLI.Program.RunCli(cliArgs.ToArray());
    }
    catch (Exception ex)
    {
        return new CallToolResult { Content = [new TextContentBlock { Text = ex.ToString(), Type = "text" }] };
    }
    finally
    {
        Console.SetOut(origOut);
    }
    return new CallToolResult { Content = [new TextContentBlock { Text = output.ToString(), Type = "text" }] };
}
