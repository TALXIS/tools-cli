using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.DependencyInjection;
using TALXIS.CLI.Config.Model;
using TALXIS.CLI.Config.Storage;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Shared;

namespace TALXIS.CLI.Config.Commands.Profile;

/// <summary>
/// <c>txc config profile create &lt;name&gt;</c> — bind an existing
/// credential (<c>--auth</c>) to an existing connection
/// (<c>--connection</c>). Inline-create shortcuts are intentionally
/// out of scope for v1 per plan scope decision: keeps the option
/// surface narrow and avoids duplicating validation from <c>auth
/// login</c> / <c>connection create</c>.
///
/// <para>
/// First profile created is auto-promoted to the global active profile
/// (mirrors pac-auth-create convenience). Subsequent creates do not
/// touch the active pointer.
/// </para>
/// </summary>
[CliCommand(
    Name = "create",
    Description = "Create a profile binding an existing --auth to an existing --connection."
)]
public class ProfileCreateCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(ProfileCreateCliCommand));

    [CliArgument(Description = "Profile name (slug). Used as --profile on leaf commands.")]
    public required string Name { get; set; }

    [CliOption(Name = "--auth", Description = "Credential alias (see 'config auth list').", Required = true)]
    public required string Auth { get; set; }

    [CliOption(Name = "--connection", Description = "Connection name (see 'config connection list').", Required = true)]
    public required string Connection { get; set; }

    [CliOption(Name = "--description", Description = "Free-form label shown in 'config profile list'.", Required = false)]
    public string? Description { get; set; }

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var name = Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            _logger.LogError("Profile name must not be empty.");
            return 1;
        }

        try
        {
            var profileStore = TxcServices.Get<IProfileStore>();
            var connectionStore = TxcServices.Get<IConnectionStore>();
            var credentialStore = TxcServices.Get<ICredentialStore>();
            var globalConfig = TxcServices.Get<IGlobalConfigStore>();

            var credential = await credentialStore.GetAsync(Auth, ct).ConfigureAwait(false);
            if (credential is null)
            {
                _logger.LogError("Credential '{Alias}' not found. Run 'txc config auth list'.", Auth);
                return 2;
            }

            var connection = await connectionStore.GetAsync(Connection, ct).ConfigureAwait(false);
            if (connection is null)
            {
                _logger.LogError("Connection '{Name}' not found. Run 'txc config connection list'.", Connection);
                return 2;
            }

            var profile = new Model.Profile
            {
                Id = name,
                ConnectionRef = connection.Id,
                CredentialRef = credential.Id,
                Description = Description,
            };

            await profileStore.UpsertAsync(profile, ct).ConfigureAwait(false);

            // Auto-promote the very first profile to active so users don't need a
            // separate `profile select` right after their first `create`.
            var global = await globalConfig.LoadAsync(ct).ConfigureAwait(false);
            var promoted = false;
            if (string.IsNullOrWhiteSpace(global.ActiveProfile))
            {
                global.ActiveProfile = profile.Id;
                await globalConfig.SaveAsync(global, ct).ConfigureAwait(false);
                promoted = true;
                _logger.LogInformation("Profile '{Id}' is now the active profile.", profile.Id);
            }

            _logger.LogInformation(
                "Profile '{Id}' saved (auth='{Auth}', connection='{Connection}').",
                profile.Id, credential.Id, connection.Id);

            OutputWriter.WriteLine(JsonSerializer.Serialize(
                new
                {
                    id = profile.Id,
                    connectionRef = profile.ConnectionRef,
                    credentialRef = profile.CredentialRef,
                    description = profile.Description,
                    active = promoted,
                },
                TxcJsonOptions.Default));
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create profile '{Name}'.", Name);
            return 1;
        }
    }
}
