using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Config.Profile;

/// <summary>
/// <c>txc config profile update &lt;name&gt;</c> — rebind an existing
/// profile to a different credential (<c>--auth</c>), connection
/// (<c>--connection</c>) or tweak its description. At least one option
/// must be supplied; no-ops are refused with exit 1 so scripts fail
/// loudly instead of silently doing nothing.
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "update",
    Description = "Rebind a profile to a different auth/connection or update its description."
)]
public class ProfileUpdateCliCommand : TxcLeafCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(ProfileUpdateCliCommand));
    protected override ILogger Logger => _logger;

    [CliArgument(Description = "Profile name.")]
    public required string Name { get; set; }

    [CliOption(Name = "--auth", Description = "New credential alias.", Required = false)]
    public string? Auth { get; set; }

    [CliOption(Name = "--connection", Description = "New connection name.", Required = false)]
    public string? Connection { get; set; }

    [CliOption(Name = "--description", Description = "New description. Pass an empty string to clear.", Required = false)]
    public string? Description { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            _logger.LogError("Profile name must be provided.");
            return ExitError;
        }

        if (Auth is null && Connection is null && Description is null)
        {
            _logger.LogError("Nothing to update. Pass --auth, --connection, or --description.");
            return ExitError;
        }

        var profileStore = TxcServices.Get<IProfileStore>();
        var connectionStore = TxcServices.Get<IConnectionStore>();
        var credentialStore = TxcServices.Get<ICredentialStore>();

        var existing = await profileStore.GetAsync(Name, CancellationToken.None).ConfigureAwait(false);
        if (existing is null)
        {
            _logger.LogError("Profile '{Name}' not found.", Name);
            return ExitValidationError;
        }

        if (Auth is not null)
        {
            var cred = await credentialStore.GetAsync(Auth, CancellationToken.None).ConfigureAwait(false);
            if (cred is null)
            {
                _logger.LogError("Credential '{Alias}' not found.", Auth);
                return ExitValidationError;
            }
            existing.CredentialRef = cred.Id;
        }

        if (Connection is not null)
        {
            var conn = await connectionStore.GetAsync(Connection, CancellationToken.None).ConfigureAwait(false);
            if (conn is null)
            {
                _logger.LogError("Connection '{Name}' not found.", Connection);
                return ExitValidationError;
            }
            existing.ConnectionRef = conn.Id;
        }

        if (Description is not null)
        {
            existing.Description = string.IsNullOrEmpty(Description) ? null : Description;
        }

        await profileStore.UpsertAsync(existing, CancellationToken.None).ConfigureAwait(false);
        _logger.LogInformation("Profile '{Id}' updated.", existing.Id);

        OutputFormatter.WriteData(existing);
        return ExitSuccess;
    }
}
