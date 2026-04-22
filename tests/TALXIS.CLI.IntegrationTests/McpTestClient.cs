using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace TALXIS.CLI.IntegrationTests;

/// <summary>
/// Singleton MCP client wrapper for testing purposes. Reuses the same connection across all tests.
/// </summary>
public sealed class McpTestClient : IAsyncDisposable
{
    private static readonly Lazy<Task<McpTestClient>> _instance = new(CreateInstanceAsync);
    private readonly McpClient _client;

    private McpTestClient(McpClient client)
    {
        _client = client;
    }

    public static Task<McpTestClient> InstanceAsync => _instance.Value;

    private static async Task<McpTestClient> CreateInstanceAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var mcpProjectPath = GetMcpProjectPath();

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "TALXIS CLI MCP Test Client",
            Command = "dotnet",
            Arguments = ["run", "--project", mcpProjectPath, "--no-build"]
        });

        var client = await McpClient.CreateAsync(transport, cancellationToken: cts.Token);
        return new McpTestClient(client);
    }

    public async Task<CallToolResult> CallToolAsync(string toolName, IReadOnlyDictionary<string, object?>? arguments = null)
    {
        arguments ??= new Dictionary<string, object?>();
        return await _client.CallToolAsync(toolName, arguments);
    }

    public async Task<IList<McpClientTool>> ListToolsAsync()
    {
        return await _client.ListToolsAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
    }

    private static string GetMcpProjectPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(baseDir);

        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TALXIS.CLI.sln")))
            dir = dir.Parent;

        if (dir == null)
            throw new InvalidOperationException("Could not find repository root");

        return Path.Combine(dir.FullName, "src", "TALXIS.CLI.MCP", "TALXIS.CLI.MCP.csproj");
    }
}
