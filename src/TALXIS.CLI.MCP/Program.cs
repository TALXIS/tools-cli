using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Reflection;
using System.Text.Json;
using TALXIS.CLI;

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

// Dynamically list all CLI commands and subcommands as MCP tools
static ValueTask<ListToolsResult> ListToolsAsync(RequestContext<ListToolsRequestParams> ctx, CancellationToken ct)
{
    var toolDefs = new List<Tool>();
    var rootType = typeof(TxcCliCommand);
    var rootCommands = new[] { rootType };
    foreach (var cmdType in rootCommands)
    {
        AddCommandAndChildren(cmdType, toolDefs, parentName: null, rootType: rootType);
    }
    return ValueTask.FromResult(new ListToolsResult { Tools = toolDefs });
}

static void AddCommandAndChildren(Type cmdType, List<Tool> defs, string? parentName, Type rootType)
{
    var attr = cmdType.GetCustomAttribute(typeof(DotMake.CommandLine.CliCommandAttribute)) as DotMake.CommandLine.CliCommandAttribute;
    if (attr == null) return;
    var name = attr.Name ?? cmdType.Name.Replace("CliCommand", "").ToLowerInvariant();
    bool isRoot = cmdType == rootType;
    bool isDirectChildOfRoot = parentName == null && isRoot == false;
    bool isGroup = attr.Children != null && attr.Children.Length > 0;
    // If parent is root, don't include its name in the tool name
    var fullName = (parentName == null || parentName == (rootType.GetCustomAttribute(typeof(DotMake.CommandLine.CliCommandAttribute)) as DotMake.CommandLine.CliCommandAttribute)?.Name || parentName == rootType.Name.Replace("CliCommand", "").ToLowerInvariant())
        ? name : $"{parentName}-{name}";
    // Only register as a tool if it's not the root, not a direct child of root, and not a group
    if (!isGroup && !isRoot && !isDirectChildOfRoot)
    {
        var tool = new Tool
        {
            Name = fullName,
            Description = attr.Description,
            InputSchema = BuildInputSchema(cmdType)
        };
        defs.Add(tool);
    }
    if (attr.Children != null)
    {
        foreach (var child in attr.Children)
            AddCommandAndChildren(child, defs, fullName, rootType);
    }
}

static JsonElement BuildInputSchema(Type cmdType)
{
    var props = cmdType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Select(p => (p, attr: p.GetCustomAttribute(typeof(DotMake.CommandLine.CliOptionAttribute)) as DotMake.CommandLine.CliOptionAttribute))
        .Where(x => x.attr != null)
        .ToList();
    var required = props.Where(x => x.attr != null && x.attr.Required)
        .Select(x => (x.attr!.Name ?? x.p.Name).TrimStart('-')).ToList();
    var properties = new Dictionary<string, object?>();
    foreach (var (p, attr) in props)
    {
        if (attr == null) continue;
        var type = p.PropertyType == typeof(bool) ? "boolean" : "string";
        var optionName = (attr.Name ?? p.Name).TrimStart('-');
        properties[optionName] = new Dictionary<string, object?>
        {
            ["type"] = type,
            ["description"] = attr.Description
        };
    }
    var schema = new Dictionary<string, object?>
    {
        ["type"] = "object",
        ["properties"] = properties,
        ["required"] = required
    };
    return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(schema));
}

// Call a CLI command by reconstructing args and running it
static async ValueTask<CallToolResult> CallToolAsync(RequestContext<CallToolRequestParams> ctx, CancellationToken ct)
{
    var p = ctx.Params;
    var toolName = p?.Name ?? string.Empty;
    if (string.IsNullOrEmpty(toolName))
        throw new McpException("Tool name is required.");
    // Always start traversal from TxcCliCommand
    Type? cmdType = FindCommandTypeByName(toolName, typeof(TxcCliCommand));
    if (cmdType == null)
        throw new McpException($"Tool '{toolName}' not found.");

    // Build args from params, supporting subcommands (e.g. "data server")
    var cliArgs = toolName.Split('-', StringSplitOptions.RemoveEmptyEntries).ToList();
    if (p != null && p.Arguments is not null)
    {
        foreach (var entry in p.Arguments)
        {
            var k = entry.Key;
            var v = entry.Value;
            if (v.ValueKind != JsonValueKind.Null)
                cliArgs.Add($"--{k}={v}");
        }
    }

    // Run the CLI command and capture output
    var output = new StringWriter();
    var origOut = Console.Out;
    Console.SetOut(output);
    try
    {
        // Use the CLI entry point
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


static Type? FindCommandTypeByName(string toolName, Type root)
{
    var segments = toolName.Split('-', StringSplitOptions.RemoveEmptyEntries);
    // Try matching as-is (without root segment)
    var found = FindCommandTypeBySegments(segments, 0, root);
    if (found != null)
        return found;
    // Try matching with root segment prepended
    var attr = root.GetCustomAttribute(typeof(DotMake.CommandLine.CliCommandAttribute)) as DotMake.CommandLine.CliCommandAttribute;
    var rootName = attr?.Name ?? root.Name.Replace("CliCommand", "").ToLowerInvariant();
    var withRoot = new string[] { rootName }.Concat(segments).ToArray();
    return FindCommandTypeBySegments(withRoot, 0, root);
}

static Type? FindCommandTypeBySegments(string[] segments, int index, Type type)
{
    var attr = type.GetCustomAttribute(typeof(DotMake.CommandLine.CliCommandAttribute)) as DotMake.CommandLine.CliCommandAttribute;
    var cmdName = attr?.Name ?? type.Name.Replace("CliCommand", "").ToLowerInvariant();
    if (!string.Equals(cmdName, segments[index], StringComparison.OrdinalIgnoreCase))
        return null;
    if (index == segments.Length - 1)
        return type;
    if (attr?.Children != null)
    {
        foreach (var child in attr.Children)
        {
            var found = FindCommandTypeBySegments(segments, index + 1, child);
            if (found != null) return found;
        }
    }
    return null;
}
