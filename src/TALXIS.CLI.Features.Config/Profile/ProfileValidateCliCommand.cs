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
[McpToolAnnotations(ReadOnlyHint = true)]
[CliCommand(
    Name = "validate",
    Description = "Preflight a profile with structural and live checks."
)]
public class ProfileValidateCliCommand : TxcLeafCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(ProfileValidateCliCommand));
    protected override ILogger Logger => _logger;

    [CliArgument(Description = "Profile name to validate. Defaults to the global active profile.", Required = false)]
    public string? Name { get; set; }

    [CliOption(Description = "Skip the live authenticated round-trip (WhoAmI); run structural checks only.")]
    public bool SkipLive { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var resolver = TxcServices.Get<IConfigurationResolver>();
        var providers = TxcServices.GetAll<IConnectionProvider>();
        ResolvedProfileContext context;
        try
        {
            context = await resolver.ResolveAsync(Name, CancellationToken.None).ConfigureAwait(false);
        }
        catch (ConfigurationResolutionException ex)
        {
            _logger.LogError("{Error}", ex.Message);
            return ExitValidationError;
        }

        if (context.Profile is null)
        {
            _logger.LogError("Resolved configuration is ephemeral. 'txc config profile validate' requires a stored profile.");
            return ExitValidationError;
        }

        var profile = context.Profile;
        var connection = context.Connection;
        var credential = context.Credential;

        var provider = providers.FirstOrDefault(p => p.ProviderKind == connection.Provider);
        if (provider is null)
        {
            _logger.LogError("Provider '{Provider}' is not registered in this build. Dataverse is the only provider shipped in v1.", connection.Provider);
            return ExitValidationError;
        }

        var mode = SkipLive ? ValidationMode.Structural : ValidationMode.Live;
        try
        {
            await provider.ValidateAsync(connection, credential, mode, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Validation failed for profile '{Profile}' ({Mode}).", profile.Id, mode);
            return ExitError;
        }

        OutputFormatter.WriteData(new
        {
            profile = profile.Id,
            connection = connection.Id,
            credential = credential.Id,
            provider = connection.Provider.ToString().ToLowerInvariant(),
            mode = mode.ToString().ToLowerInvariant(),
            status = "ok",
        });
        return ExitSuccess;
    }
}
