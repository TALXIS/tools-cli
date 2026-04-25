using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Config.Profile;

/// <summary>
/// <c>txc config profile list</c> — JSON dump of all profiles. Each
/// entry carries an <c>active</c> flag so scripts can skip running
/// <c>config profile show</c> to figure out which one is current.
/// </summary>
[CliCommand(
    Name = "list",
    Description = "List profiles as JSON."
)]
public class ProfileListCliCommand : TxcLeafCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(ProfileListCliCommand));
    protected override ILogger Logger => _logger;

    protected override async Task<int> ExecuteAsync()
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
        }).ToList();

        OutputFormatter.WriteList(projected);
        return ExitSuccess;
    }
}
