using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.DependencyInjection;
using TALXIS.CLI.Config.Storage;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Shared;

namespace TALXIS.CLI.Config.Commands.Profile;

/// <summary>
/// <c>txc config profile list</c> — JSON dump of all profiles. Each
/// entry carries an <c>active</c> flag so scripts can skip running
/// <c>config profile show</c> to figure out which one is current.
/// </summary>
[CliCommand(
    Name = "list",
    Description = "List profiles as JSON."
)]
public class ProfileListCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(ProfileListCliCommand));

    public async Task<int> RunAsync()
    {
        try
        {
            var profileStore = TxcServices.Get<IProfileStore>();
            var globalConfig = TxcServices.Get<IGlobalConfigStore>();

            var profiles = await profileStore.ListAsync(CancellationToken.None).ConfigureAwait(false);
            var global = await globalConfig.LoadAsync(CancellationToken.None).ConfigureAwait(false);
            var active = global.ActiveProfile;

            var projected = profiles.Select(p => new
            {
                id = p.Id,
                connectionRef = p.ConnectionRef,
                credentialRef = p.CredentialRef,
                description = p.Description,
                active = string.Equals(p.Id, active, StringComparison.OrdinalIgnoreCase),
            });

            OutputWriter.WriteLine(JsonSerializer.Serialize(projected, TxcJsonOptions.Default));
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list profiles.");
            return 1;
        }
    }
}
