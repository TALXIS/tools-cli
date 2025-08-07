using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace TALXIS.CLI.IntegrationTests;

/// <summary>
/// Singleton MCP client for testing purposes. Reuses the same connection across all tests.
/// </summary>
public sealed class McpClient : IAsyncDisposable
{
    private static readonly Lazy<Task<McpClient>> _instance = new(CreateInstanceAsync);
    private readonly IMcpClient _client;

    private McpClient(IMcpClient client)
    {
        _client = client;
    }

    public static Task<McpClient> InstanceAsync => _instance.Value;

    private static async Task<McpClient> CreateInstanceAsync()
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "TALXIS CLI MCP Test Client",
            Command = "dotnet",
            Arguments = ["run", "--project", GetMcpProjectPath(), "--no-build"]
        });

        var client = await McpClientFactory.CreateAsync(transport);
        return new McpClient(client);
    }

    public async Task<CallToolResult> CallToolAsync(string toolName, Dictionary<string, object> arguments = null)
    {
        arguments = arguments ?? new Dictionary<string, object>();
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
        var dir = new System.IO.DirectoryInfo(baseDir);
        
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "TALXIS.CLI.sln")))
        {
            dir = dir.Parent;
        }
        
        if (dir == null)
            throw new InvalidOperationException("Could not find repository root");
            
        return System.IO.Path.Combine(dir.FullName, "src", "TALXIS.CLI.MCP", "TALXIS.CLI.MCP.csproj");
    }
}
