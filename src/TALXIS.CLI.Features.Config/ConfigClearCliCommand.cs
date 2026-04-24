using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Resolution;
using TALXIS.CLI.Core.Storage;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Config;

/// <summary>
/// <c>txc config clear</c> — removes txc's persisted local state: stored
/// profiles, connections, credentials, settings, secret vault, MSAL token
/// cache, and the workspace pin discoverable from the current directory.
/// </summary>
[McpIgnore]
[CliCommand(
    Name = "clear",
    Description = "Remove all persisted txc configuration and auth state from this device."
)]
public sealed class ConfigClearCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(ConfigClearCliCommand));

    public async Task<int> RunAsync()
    {
        try
        {
            var paths = TxcServices.Get<ConfigPaths>();
            var env = TxcServices.Get<IEnvironmentReader>();
            var workspace = TxcServices.Get<IWorkspaceDiscovery>();
            var vault = TxcServices.Get<ICredentialVault>();
            var tokenCache = TxcServices.Get<ITokenCacheStore>();

            await RemoveCurrentWorkspacePinAsync(workspace, env.GetCurrentDirectory()).ConfigureAwait(false);

            await vault.ClearAsync(CancellationToken.None).ConfigureAwait(false);
            tokenCache.Clear();

            if (Directory.Exists(paths.Root))
            {
                Directory.Delete(paths.Root, recursive: true);
                _logger.LogDebug("Removed config root '{Path}'.", paths.Root);
            }

            _logger.LogInformation("Cleared txc local configuration at '{Path}'.", paths.Root);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear txc local configuration.");
            return 1;
        }
    }

    private async Task RemoveCurrentWorkspacePinAsync(IWorkspaceDiscovery workspace, string currentDirectory)
    {
        var resolution = await workspace.DiscoverAsync(currentDirectory, CancellationToken.None).ConfigureAwait(false);
        if (resolution is null || !File.Exists(resolution.WorkspaceFilePath))
            return;

        File.Delete(resolution.WorkspaceFilePath);
        _logger.LogInformation("Removed workspace pin at '{Path}'.", resolution.WorkspaceFilePath);

        var workspaceDir = Path.GetDirectoryName(resolution.WorkspaceFilePath);
        if (!string.IsNullOrEmpty(workspaceDir) &&
            Directory.Exists(workspaceDir) &&
            !Directory.EnumerateFileSystemEntries(workspaceDir).Any())
        {
            Directory.Delete(workspaceDir);
            _logger.LogDebug("Removed empty '{Dir}'.", workspaceDir);
        }
    }

}
