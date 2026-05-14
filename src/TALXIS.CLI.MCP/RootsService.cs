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
        if (System.Threading.Volatile.Read(ref _resolved))
            return System.Threading.Volatile.Read(ref _cachedWorkingDirectory);

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (System.Threading.Volatile.Read(ref _resolved))
                return System.Threading.Volatile.Read(ref _cachedWorkingDirectory);

            System.Threading.Volatile.Write(ref _cachedWorkingDirectory, await ResolveWorkingDirectoryAsync(cancellationToken));
            // Publish completion with release semantics so readers that observe
            // _resolved == true also observe the cached working directory.
            System.Threading.Volatile.Write(ref _resolved, true);
            return System.Threading.Volatile.Read(ref _cachedWorkingDirectory);
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
        {
            _logger?.LogWarning("MCP client returned no workspace roots. Relative paths will resolve against the server process directory ({Cwd}). Use absolute paths to avoid unexpected resolution.", Environment.CurrentDirectory);
            return null;
        }

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

        // Uri.LocalPath handles percent-decoding and basic conversion, but
        // Path.GetFullPath normalises separators and resolves platform
        // differences (e.g. stripping the leading '/' from '/C:/...' on
        // Windows, converting '/' → '\\', etc.).
        try
        {
            return Path.GetFullPath(parsed.LocalPath);
        }
        catch (Exception)
        {
            // Malformed URI path that the OS cannot normalise (e.g. invalid
            // characters for the current platform). Fall back to null so the
            // server uses its own CWD rather than crashing.
            return null;
        }
    }
}
