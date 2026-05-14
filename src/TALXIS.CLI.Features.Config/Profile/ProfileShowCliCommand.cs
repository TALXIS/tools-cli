using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Config.Profile;

/// <summary>
/// <c>txc config profile show [&lt;name&gt;]</c> — "whoami"-style detail
/// for a single profile. Without <c>&lt;name&gt;</c> shows the active
/// profile (as pointed to by <c>config.json</c>). Expands the linked
/// connection + credential inline so users can see everything in one
/// blob without three round-trips.
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "get",
    Description = "Get a profile with its expanded connection + credential. Defaults to the active profile."
)]
public class ProfileShowCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ProfileShowCliCommand));

    [CliArgument(Description = "Profile name. If omitted, shows the active profile.", Required = false)]
    public string? Name { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var profileStore = TxcServices.Get<IProfileStore>();
        var connectionStore = TxcServices.Get<IConnectionStore>();
        var credentialStore = TxcServices.Get<ICredentialStore>();
        var globalConfig = TxcServices.Get<IGlobalConfigStore>();

        string? target = Name;
        var global = await globalConfig.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(target))
        {
            target = global.ActiveProfile;
            if (string.IsNullOrWhiteSpace(target))
            {
                Logger.LogError("No active profile is set. Pass <name> or run 'txc config profile select <name>'.");
                return ExitValidationError;
            }
        }

        var profile = await profileStore.GetAsync(target!, CancellationToken.None).ConfigureAwait(false);
        if (profile is null)
        {
            Logger.LogError("Profile '{Name}' not found.", target);
            return ExitValidationError;
        }

        // Expand refs so a single `show` gives callers the full picture.
        // Missing refs are surfaced as null rather than erroring — `validate` is the command for integrity checks.
        var connection = await connectionStore.GetAsync(profile.ConnectionRef, CancellationToken.None).ConfigureAwait(false);
        var credential = await credentialStore.GetAsync(profile.CredentialRef, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteData(new
        {
            id = profile.Id,
            active = string.Equals(profile.Id, global.ActiveProfile, StringComparison.OrdinalIgnoreCase),
            description = profile.Description,
            connection,
            credential,
        });
        return ExitSuccess;
    }
}
