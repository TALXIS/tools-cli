using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Config.DependencyInjection;
using TALXIS.CLI.Config.Resolution;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Config.Commands.Profile;

/// <summary>
/// <c>txc config profile unpin</c> — removes
/// <c>&lt;cwd&gt;/.txc/workspace.json</c>. Idempotent: missing file is
/// not an error (exit 0 with an informational log) so repeated unpin
/// calls don't break scripts. Also removes the empty <c>.txc</c>
/// directory when the workspace file was the only thing inside it.
/// </summary>
[CliCommand(
    Name = "unpin",
    Description = "Remove <cwd>/.txc/workspace.json (no-op if absent)."
)]
public class ProfileUnpinCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(ProfileUnpinCliCommand));

    public Task<int> RunAsync()
    {
        try
        {
            var env = TxcServices.Get<IEnvironmentReader>();
            var cwd = env.GetCurrentDirectory();
            var workspaceDir = Path.Combine(cwd, WorkspaceDiscovery.DirectoryName);
            var workspaceFile = Path.Combine(workspaceDir, WorkspaceDiscovery.FileName);

            if (!File.Exists(workspaceFile))
            {
                _logger.LogInformation("No workspace pin found at '{Path}'. Nothing to do.", workspaceFile);
                return Task.FromResult(0);
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

            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unpin workspace profile.");
            return Task.FromResult(1);
        }
    }
}
