using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace TALXIS.CLI.MCP;

internal sealed class RootsService
{
    private readonly McpServer _server;
    private readonly ILogger? _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private string? _cachedWorkingDirectory;
    private bool _resolved;

    public RootsService(McpServer server, ILogger? logger = null)
    {
        _server = server;
        _logger = logger;
    }

    public async ValueTask<string?> GetWorkingDirectoryAsync(CancellationToken cancellationToken)
    {
        if (_resolved)
            return _cachedWorkingDirectory;

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (_resolved)
                return _cachedWorkingDirectory;

            _cachedWorkingDirectory = await ResolveWorkingDirectoryAsync(cancellationToken);
            _resolved = true;
            return _cachedWorkingDirectory;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<string?> ResolveWorkingDirectoryAsync(CancellationToken cancellationToken)
    {
        ListRootsResult result;
        try
        {
            result = await _server.RequestRootsAsync(new ListRootsRequestParams(), cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // Client doesn't support roots capability
            _logger?.LogDebug("Client does not support roots capability.");
            return null;
        }
        catch (McpException ex)
        {
            _logger?.LogWarning(ex, "Failed to request roots from client.");
            return null;
        }

        if (result.Roots is not { Count: > 0 })
            return null;

        return ConvertFileUriToPath(result.Roots[0].Uri);
    }

    internal static string? ConvertFileUriToPath(string? uri)
    {
        if (string.IsNullOrEmpty(uri))
            return null;

        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            return null;

        if (!string.Equals(parsed.Scheme, "file", StringComparison.OrdinalIgnoreCase))
            return null;

        return parsed.LocalPath;
    }
}
