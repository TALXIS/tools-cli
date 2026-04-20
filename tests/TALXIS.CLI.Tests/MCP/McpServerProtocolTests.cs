#pragma warning disable MCPEXP001

using System.IO.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TALXIS.CLI.MCP;
using Xunit;

namespace TALXIS.CLI.Tests.MCP;

/// <summary>
/// Protocol-level tests using in-memory Pipe transport (no subprocess).
/// Inspired by the MCP SDK's ClientServerTestBase pattern.
/// </summary>
public class McpServerProtocolTests : IAsyncDisposable
{
    private readonly Pipe _clientToServer = new();
    private readonly Pipe _serverToClient = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly McpToolRegistry _registry = new();
    private readonly ServiceProvider _serviceProvider;
    private readonly McpServer _server;
    private readonly Task _serverTask;

    public McpServerProtocolTests()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));

        services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation { Name = "txc-mcp-test", Version = "0.0.0" };
            options.TaskStore = new InMemoryMcpTaskStore(
                defaultTtl: TimeSpan.FromMinutes(5),
                pollInterval: TimeSpan.FromMilliseconds(100)
            );
            options.SendTaskStatusNotifications = true;
            options.Capabilities = new ServerCapabilities
            {
                Tools = new ToolsCapability { ListChanged = true },
                Logging = new LoggingCapability { }
            };
        })
        .WithStreamServerTransport(
            _clientToServer.Reader.AsStream(),
            _serverToClient.Writer.AsStream())
        .WithListToolsHandler((ctx, ct) =>
            ValueTask.FromResult(new ListToolsResult { Tools = _registry.ListTools() }))
        .WithCallToolHandler(async (ctx, ct) =>
        {
            var toolName = ctx.Params?.Name ?? string.Empty;
            var cmdType = _registry.FindCommandTypeByToolName(toolName);
            if (cmdType == null)
                return new CallToolResult
                {
                    Content = [new TextContentBlock { Text = $"Tool '{toolName}' not found." }],
                    IsError = true
                };

            // For protocol tests, only handle the in-process copilot-instructions tool
            if (toolName == "copilot-instructions")
            {
                var output = new StringWriter();
                using var redirect = TALXIS.CLI.Shared.OutputWriter.RedirectTo(output);
                var command = new CopilotInstructionsCliCommand();
                await command.RunAsync(null!);
                return new CallToolResult
                {
                    Content = [new TextContentBlock { Text = output.ToString() }]
                };
            }

            // For other tools, just return a placeholder (we don't want subprocess in unit tests)
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = $"Tool '{toolName}' found but not executed in test" }]
            };
        });

        _serviceProvider = services.BuildServiceProvider();
        _server = _serviceProvider.GetRequiredService<McpServer>();
        _serverTask = _server.RunAsync(_cts.Token);
    }

    private async Task<McpClient> CreateClientAsync()
    {
        return await McpClient.CreateAsync(
            new StreamClientTransport(
                serverInput: _clientToServer.Writer.AsStream(),
                _serverToClient.Reader.AsStream()),
            cancellationToken: _cts.Token);
    }

    [Fact]
    public async Task ListTools_ViaProtocol_ReturnsTools()
    {
        await using var client = await CreateClientAsync();
        var tools = await client.ListToolsAsync(cancellationToken: _cts.Token);

        Assert.NotEmpty(tools);
        Assert.Contains(tools, t => t.Name == "workspace_component_type_list");
        Assert.Contains(tools, t => t.Name == "copilot-instructions");
    }

    [Fact]
    public async Task CallTool_InProcessTool_ReturnsResult()
    {
        await using var client = await CreateClientAsync();
        var result = await client.CallToolAsync("copilot-instructions", cancellationToken: _cts.Token);

        Assert.NotNull(result.Content);
        Assert.NotEmpty(result.Content);
        Assert.True(result.IsError != true);

        if (result.Content[0] is TextContentBlock textBlock)
        {
            // copilot-instructions returns markdown content
            Assert.False(string.IsNullOrWhiteSpace(textBlock.Text));
        }
    }

    [Fact]
    public async Task CallTool_UnknownTool_ReturnsError()
    {
        await using var client = await CreateClientAsync();
        var result = await client.CallToolAsync("nonexistent_tool_xyz", cancellationToken: _cts.Token);

        Assert.True(result.IsError);
        Assert.Contains(result.Content, c => c is TextContentBlock tb && tb.Text.Contains("not found"));
    }

    [Fact]
    public async Task CallTool_WithProgressToken_DoesNotError()
    {
        // TODO: The high-level McpClient.CallToolAsync API does not expose a progressToken parameter.
        // To fully test progress wiring at the protocol level, a raw JSON-RPC message with _meta.progressToken
        // would be needed. For now, we verify the tool call succeeds and the server doesn't error when
        // the copilot-instructions tool is invoked (progress is only emitted for subprocess-based tools).
        await using var client = await CreateClientAsync();
        var result = await client.CallToolAsync("copilot-instructions", cancellationToken: _cts.Token);

        Assert.NotNull(result.Content);
        Assert.NotEmpty(result.Content);
        Assert.True(result.IsError != true);
    }

    [Fact]
    public async Task ListTools_AllToolsHaveDescriptions()
    {
        await using var client = await CreateClientAsync();
        var tools = await client.ListToolsAsync(cancellationToken: _cts.Token);

        foreach (var tool in tools)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Description), $"Tool '{tool.Name}' missing description");
        }
    }

    [Fact]
    public async Task ListTools_LongRunningToolsHaveTaskSupport()
    {
        var tools = _registry.ListTools();

        var deployTool = tools.FirstOrDefault(t => t.Name == "environment_deploy");
        Assert.NotNull(deployTool);
        Assert.NotNull(deployTool.Execution);
        Assert.NotNull(deployTool.Execution.TaskSupport);

        var importTool = tools.FirstOrDefault(t => t.Name == "data_package_import");
        Assert.NotNull(importTool);
        Assert.NotNull(importTool.Execution);
        Assert.NotNull(importTool.Execution.TaskSupport);
    }

    [Fact]
    public async Task ListTools_ShortLivedToolsDoNotHaveTaskSupport()
    {
        var tools = _registry.ListTools();

        var docsTool = tools.FirstOrDefault(t => t.Name == "docs");
        Assert.NotNull(docsTool);
        Assert.Null(docsTool.Execution);

        var copilotTool = tools.FirstOrDefault(t => t.Name == "copilot-instructions");
        Assert.NotNull(copilotTool);
        Assert.Null(copilotTool.Execution);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _clientToServer.Writer.Complete();
        _serverToClient.Writer.Complete();

        try { await _serverTask; } catch (OperationCanceledException) { }

        await _serviceProvider.DisposeAsync();
        _cts.Dispose();
    }
}
