using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Resolution;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Config.Profile;

/// <summary>
/// <c>txc config profile unpin</c> — removes
/// <c>&lt;cwd&gt;/.txc/workspace.json</c>. Idempotent: missing file is
/// not an error (exit 0 with an informational log) so repeated unpin
/// calls don't break scripts. Also removes the empty <c>.txc</c>
/// directory when the workspace file was the only thing inside it.
/// </summary>
[McpToolAnnotations(DestructiveHint = true, IdempotentHint = true)]
[CliCommand(
    Name = "unpin",
    Description = "Remove <cwd>/.txc/workspace.json (no-op if absent)."
)]
public class ProfileUnpinCliCommand : TxcLeafCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(ProfileUnpinCliCommand));
    protected override ILogger Logger => _logger;

    protected override Task<int> ExecuteAsync()
    {
        var env = TxcServices.Get<IEnvironmentReader>();
        var cwd = env.GetCurrentDirectory();
        var workspaceDir = Path.Combine(cwd, WorkspaceDiscovery.DirectoryName);
        var workspaceFile = Path.Combine(workspaceDir, WorkspaceDiscovery.FileName);

        if (!File.Exists(workspaceFile))
        {
            _logger.LogInformation("No workspace pin found at '{Path}'. Nothing to do.", workspaceFile);
            OutputFormatter.WriteResult("succeeded", "No workspace pin found. Nothing to do.");
            return Task.FromResult(ExitSuccess);
        }

        File.Delete(workspaceFile);
        _logger.LogInformation("Removed workspace pin at '{Path}'.", workspaceFile);

        // Clean up the `.txc` directory when it's empty so `ls` stays tidy.
        if (Directory.Exists(workspaceDir) &&
            !Directory.EnumerateFileSystemEntries(workspaceDir).Any())
        {
            Directory.Delete(workspaceDir);
            _logger.LogDebug("Removed empty '{Dir}'.", workspaceDir);
        }

        OutputFormatter.WriteResult("succeeded", $"Workspace pin removed at '{workspaceFile}'.");
        return Task.FromResult(ExitSuccess);
    }
}
