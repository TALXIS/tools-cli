using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Storage;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Core;

namespace TALXIS.CLI.Features.Config.Profile;

/// <summary>
/// <c>txc config profile show [&lt;name&gt;]</c> — "whoami"-style detail
/// for a single profile. Without <c>&lt;name&gt;</c> shows the active
/// profile (as pointed to by <c>config.json</c>). Expands the linked
/// connection + credential inline so users can see everything in one
/// blob without three round-trips.
/// </summary>
[CliCommand(
    Name = "show",
    Description = "Show a profile with its expanded connection + credential. Defaults to the active profile."
)]
public class ProfileShowCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(ProfileShowCliCommand));

    [CliArgument(Description = "Profile name. If omitted, shows the active profile.", Required = false)]
    public string? Name { get; set; }

    public async Task<int> RunAsync()
    {
        try
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
                    _logger.LogError("No active profile is set. Pass <name> or run 'txc config profile select <name>'.");
                    return 2;
                }
            }

            var profile = await profileStore.GetAsync(target!, CancellationToken.None).ConfigureAwait(false);
            if (profile is null)
            {
                _logger.LogError("Profile '{Name}' not found.", target);
                return 2;
            }

            // Expand refs so a single `show` gives callers the full picture.
            // Missing refs are surfaced as null rather than erroring — `validate` is the command for integrity checks.
            var connection = await connectionStore.GetAsync(profile.ConnectionRef, CancellationToken.None).ConfigureAwait(false);
            var credential = await credentialStore.GetAsync(profile.CredentialRef, CancellationToken.None).ConfigureAwait(false);

            OutputWriter.WriteLine(JsonSerializer.Serialize(
                new
                {
                    id = profile.Id,
                    active = string.Equals(profile.Id, global.ActiveProfile, StringComparison.OrdinalIgnoreCase),
                    description = profile.Description,
                    connection,
                    credential,
                },
                TxcJsonOptions.Default));
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show profile.");
            return 1;
        }
    }
}
