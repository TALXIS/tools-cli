using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Logging;


namespace TALXIS.CLI.Features.Config.Profile;

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
[CliReadOnly]
[CliCommand(
    Name = "validate",
    Description = "Preflight a profile with structural and live checks."
)]
public class ProfileValidateCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ProfileValidateCliCommand));

    [CliArgument(Description = "Profile name to validate. Defaults to the global active profile.", Required = false)]
    public string? Name { get; set; }

    [CliOption(Description = "Skip the live authenticated round-trip (WhoAmI); run structural checks only.")]
    public bool SkipLive { get; set; }

    [CliOption(Name = "--refresh-env-type", Description = "Query the Power Platform catalog to refresh the environment type (Production/Sandbox/etc.) on the connection.")]
    public bool RefreshEnvType { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var resolver = TxcServices.Get<IConfigurationResolver>();
        var providers = TxcServices.GetAll<IConnectionProvider>();
        var context = await resolver.ResolveAsync(Name, CancellationToken.None).ConfigureAwait(false);

        if (context.Profile is null)
        {
            Logger.LogError("Resolved configuration is ephemeral. 'txc config profile validate' requires a stored profile.");
            return ExitValidationError;
        }

        var profile = context.Profile;
        var connection = context.Connection;
        var credential = context.Credential;

        var provider = providers.FirstOrDefault(p => p.ProviderKind == connection.Provider);
        if (provider is null)
        {
            Logger.LogError("Provider '{Provider}' is not registered in this build. Dataverse is the only provider shipped in v1.", connection.Provider);
            return ExitValidationError;
        }

        var mode = SkipLive ? ValidationMode.Structural : ValidationMode.Live;
        await provider.ValidateAsync(connection, credential, mode, CancellationToken.None).ConfigureAwait(false);

        EnvironmentType? refreshedEnvType = connection.EnvironmentType;
        if (RefreshEnvType)
        {
            refreshedEnvType = await TryRefreshEnvironmentTypeAsync(connection, credential).ConfigureAwait(false);
        }

        OutputFormatter.WriteData(new
        {
            profile = profile.Id,
            connection = connection.Id,
            credential = credential.Id,
            provider = connection.Provider.ToString().ToLowerInvariant(),
            environmentType = refreshedEnvType?.ToString().ToLowerInvariant(),
            mode = mode.ToString().ToLowerInvariant(),
            status = "ok",
        });
        return ExitSuccess;
    }

    /// <summary>
    /// Delegates to the registered <see cref="IEnvironmentTypeResolver"/> to
    /// query the provider's control plane and update the connection metadata.
    /// </summary>
    private async Task<EnvironmentType?> TryRefreshEnvironmentTypeAsync(TALXIS.CLI.Core.Model.Connection connection, Credential credential)
    {
        try
        {
            var resolver = TxcServices.Get<IEnvironmentTypeResolver>();
            return await resolver.RefreshAsync(connection, credential, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to refresh environment type. Current value unchanged.");
            return connection.EnvironmentType;
        }
    }
}
