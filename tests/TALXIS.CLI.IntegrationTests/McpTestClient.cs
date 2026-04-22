using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        // Build the MCP project first to ensure it's available
        await BuildMcpProjectAsync(mcpProjectPath, cts.Token);

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "TALXIS CLI MCP Test Client",
            Command = "dotnet",
            Arguments = ["run", "--project", mcpProjectPath]
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

    /// <summary>
    /// Builds the MCP project to ensure it's available for testing.
    /// This is especially important in CI environments where --no-build might not work reliably.
    /// </summary>
    private static async Task BuildMcpProjectAsync(string projectPath, CancellationToken cancellationToken)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{projectPath}\" --configuration Release",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process == null)
            throw new InvalidOperationException("Failed to start dotnet build process");

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException($"Failed to build MCP project. Exit code: {process.ExitCode}. Output: {stdout}. Error: {stderr}");
        }
    }
}
