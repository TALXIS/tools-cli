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
[CliDestructive("Permanently removes all persisted txc configuration, credentials, and auth state from this device.")]
[McpIgnore]
[CliCommand(
    Name = "clear",
    Description = "Remove all persisted txc configuration and auth state from this device."
)]
public sealed class ConfigClearCliCommand : TxcLeafCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConfigClearCliCommand));

    /// <inheritdoc />
    [CliOption(Name = "--yes", Description = "Skip interactive confirmation for this destructive operation.", Required = false)]
    public bool Yes { get; set; }

    protected override async Task<int> ExecuteAsync()
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
            Logger.LogDebug("Removed config root '{Path}'.", paths.Root);
        }

        Logger.LogInformation("Cleared txc local configuration at '{Path}'.", paths.Root);
        OutputFormatter.WriteResult("succeeded", $"Cleared txc local configuration at '{paths.Root}'.");
        return ExitSuccess;
    }

    private async Task RemoveCurrentWorkspacePinAsync(IWorkspaceDiscovery workspace, string currentDirectory)
    {
        var resolution = await workspace.DiscoverAsync(currentDirectory, CancellationToken.None).ConfigureAwait(false);
        if (resolution is null || !File.Exists(resolution.WorkspaceFilePath))
            return;

        File.Delete(resolution.WorkspaceFilePath);
        Logger.LogInformation("Removed workspace pin at '{Path}'.", resolution.WorkspaceFilePath);

        var workspaceDir = Path.GetDirectoryName(resolution.WorkspaceFilePath);
        if (!string.IsNullOrEmpty(workspaceDir) &&
            Directory.Exists(workspaceDir) &&
            !Directory.EnumerateFileSystemEntries(workspaceDir).Any())
        {
            Directory.Delete(workspaceDir);
            Logger.LogDebug("Removed empty '{Dir}'.", workspaceDir);
        }
    }
}
