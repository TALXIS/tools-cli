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
/// <c>txc config profile validate [&lt;name&gt;]</c> — preflights a
/// profile so "will my next command work?" has an explicit answer
/// before long-running operations start. Without <c>&lt;name&gt;</c>
/// validates the global active profile.
///
/// <para>
/// Runs the provider's structural check (URLs, credential-kind
/// compatibility, authority wiring), then — unless <c>--skip-live</c>
/// is passed — issues a live authenticated round-trip (Dataverse
/// WhoAmI). Exit 0 = success; exit 2 = missing/unreferenced/unsupported
/// provider; exit 1 = validation failure (structural or live).
/// </para>
/// </summary>
[McpIgnore]
[CliCommand(
    Name = "validate",
    Description = "Preflight a profile with structural and live checks."
)]
public class ProfileValidateCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(ProfileValidateCliCommand));

    [CliArgument(Description = "Profile name to validate. Defaults to the global active profile.", Required = false)]
    public string? Name { get; set; }

    [CliOption(Description = "Skip the live authenticated round-trip (WhoAmI); run structural checks only.")]
    public bool SkipLive { get; set; }

    public async Task<int> RunAsync()
    {
        try
        {
            var profileStore = TxcServices.Get<IProfileStore>();
            var connectionStore = TxcServices.Get<IConnectionStore>();
            var credentialStore = TxcServices.Get<ICredentialStore>();
            var globalConfig = TxcServices.Get<IGlobalConfigStore>();
            var providers = TxcServices.GetAll<IConnectionProvider>();

            var target = Name;
            if (string.IsNullOrWhiteSpace(target))
            {
                var gc = await globalConfig.LoadAsync(CancellationToken.None).ConfigureAwait(false);
                target = gc.ActiveProfile;
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

            var connection = await connectionStore.GetAsync(profile.ConnectionRef, CancellationToken.None).ConfigureAwait(false);
            if (connection is null)
            {
                _logger.LogError("Profile '{Profile}' references missing connection '{Connection}'.", profile.Id, profile.ConnectionRef);
                return 2;
            }

            var credential = await credentialStore.GetAsync(profile.CredentialRef, CancellationToken.None).ConfigureAwait(false);
            if (credential is null)
            {
                _logger.LogError("Profile '{Profile}' references missing credential '{Credential}'.", profile.Id, profile.CredentialRef);
                return 2;
            }

            var provider = providers.FirstOrDefault(p => p.ProviderKind == connection.Provider);
            if (provider is null)
            {
                _logger.LogError("Provider '{Provider}' is not registered in this build. Dataverse is the only provider shipped in v1.", connection.Provider);
                return 2;
            }

            var mode = SkipLive ? ValidationMode.Structural : ValidationMode.Live;
            try
            {
                await provider.ValidateAsync(connection, credential, mode, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Validation failed for profile '{Profile}' ({Mode}).", profile.Id, mode);
                return 1;
            }

            OutputWriter.WriteLine(JsonSerializer.Serialize(
                new
                {
                    profile = profile.Id,
                    connection = connection.Id,
                    credential = credential.Id,
                    provider = connection.Provider.ToString().ToLowerInvariant(),
                    mode = mode.ToString().ToLowerInvariant(),
                    status = "ok",
                },
                TxcJsonOptions.Default));
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate profile.");
            return 1;
        }
    }
}
