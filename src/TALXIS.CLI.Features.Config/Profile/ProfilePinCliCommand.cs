using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Resolution;
using TALXIS.CLI.Core.Storage;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Config.Profile;

/// <summary>
/// <c>txc config profile pin [&lt;name&gt;]</c> — writes
/// <c>&lt;cwd&gt;/.txc/workspace.json</c> so the active profile is
/// automatically resolved when any <c>txc</c> command runs from this
/// tree (or any child directory). See plan §precedence — a workspace
/// pin beats the global active pointer but is still overridden by
/// <c>--profile</c> and <c>TXC_PROFILE</c>.
///
/// <para>
/// Without <c>&lt;name&gt;</c> pins the current global active profile
/// (so <c>select</c> then <c>pin</c> is the usual flow); otherwise pins
/// the named profile (must exist).
/// </para>
/// </summary>
[McpIgnore]
[CliCommand(
    Name = "pin",
    Description = "Pin the active profile (or <name>) to <cwd>/.txc/workspace.json."
)]
public class ProfilePinCliCommand : TxcLeafCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(ProfilePinCliCommand));
    protected override ILogger Logger => _logger;

    [CliArgument(Description = "Profile name to pin. Defaults to the global active profile.", Required = false)]
    public string? Name { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var profileStore = TxcServices.Get<IProfileStore>();
        var globalConfig = TxcServices.Get<IGlobalConfigStore>();
        var env = TxcServices.Get<IEnvironmentReader>();

        string? target = Name;
        if (string.IsNullOrWhiteSpace(target))
        {
            var global = await globalConfig.LoadAsync(CancellationToken.None).ConfigureAwait(false);
            target = global.ActiveProfile;
            if (string.IsNullOrWhiteSpace(target))
            {
                _logger.LogError("No active profile is set. Pass <name> or run 'txc config profile select <name>' first.");
                return ExitValidationError;
            }
        }

        var profile = await profileStore.GetAsync(target!, CancellationToken.None).ConfigureAwait(false);
        if (profile is null)
        {
            _logger.LogError("Profile '{Name}' not found.", target);
            return ExitValidationError;
        }

        var cwd = env.GetCurrentDirectory();
        var workspaceDir = Path.Combine(cwd, WorkspaceDiscovery.DirectoryName);
        var workspaceFile = Path.Combine(workspaceDir, WorkspaceDiscovery.FileName);

        var config = new WorkspaceConfig { DefaultProfile = profile.Id };
        await JsonFile.WriteAtomicAsync(workspaceFile, config, CancellationToken.None).ConfigureAwait(false);

        _logger.LogInformation("Pinned profile '{Id}' to '{Path}'.", profile.Id, workspaceFile);

        OutputFormatter.WriteData(new { profile = profile.Id, path = workspaceFile });
        return ExitSuccess;
    }
}
