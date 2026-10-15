using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Core.Resolution;
using TALXIS.CLI.Features.Environment.Connector;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Connection;

/// <summary>
/// Creates the stored sign-in of a connector. Works for any connector: no
/// authentication, API key, service principal, certificate, or user-delegated OAuth.
/// Usage: <c>txc environment connection create --connector &lt;name&gt; --display-name &lt;name&gt; [--auth-method &lt;method&gt;] [--param &lt;name=value&gt;]... [--secret-from-env &lt;name=ENV_VAR&gt;]... [--no-consent]</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "create",
    Description = "Creates a connection for a connector in the LIVE connected environment. Requires an active profile. Idempotent by connector and display name: an existing connected connection is returned unchanged. For OAuth connectors a user credential signs in silently when the connector supports it; otherwise a consent link is returned."
)]
public class ConnectionCreateCliCommand : ConnectivityCommandBase
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectionCreateCliCommand));

    [CliOption(Name = "--connector", Description = ConnectorDescriptions.Identifier + " See 'txc environment connector list'.", Required = true)]
    public string Connector { get; set; } = null!;

    [CliOption(Name = "--display-name", Description = "Display name of the connection. It identifies an existing connection, so repeating the command changes nothing.", Required = true)]
    public string DisplayName { get; set; } = null!;

    [CliOption(Name = "--auth-method", Description = "Authentication method, for example ServicePrincipalOauth. The names are defined by the connector (see 'txc environment connector get'). Defaults to the connector's only method, or its OAuth method.", Required = false)]
    public string? AuthMethod { get; set; }

    [CliOption(Name = "--param", Description = "Connection parameter as name=value. Repeat for several. Do not pass secrets here; use --secret-from-env.", Required = false)]
    public List<string>? Param { get; set; }

    [CliOption(Name = "--secret-from-env", Description = "Secret connection parameter as name=ENV_VAR: the parameter name and the environment variable that holds its value (unlike 'config auth add-service-principal', which takes only the variable name). The value is never echoed. Repeat for several.", Required = false)]
    public List<string>? SecretFromEnv { get; set; }

    [CliOption(Name = "--no-consent", Description = "Do not try to complete OAuth consent; create the connection and return the consent link.", Required = false)]
    public bool NoConsent { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var assembler = new ConnectionParameterAssembler(TxcServices.Get<IEnvironmentReader>());
        if (!assembler.TryAssemble(Param, SecretFromEnv, out var parameters, out var error))
        {
            Logger.LogError("{Error}", error);
            return ExitValidationError;
        }

        var service = TxcServices.Get<IConnectionService>();
        var outcome = await service.CreateAsync(
            Profile,
            new ConnectionCreateOptions(Connector, DisplayName, AuthMethod, parameters, AttemptConsent: !NoConsent),
            CancellationToken.None).ConfigureAwait(false);

        // The first five properties are the ones 'connection get' prints, under the same names.
        OutputFormatter.WriteData(
            new
            {
                status = outcome.Status.ToKebabCase(),
                connectionId = outcome.ConnectionId,
                apiId = outcome.ApiId,
                displayName = outcome.DisplayName,
                connectionStatus = outcome.ConnectionStatus,
                statusMessage = outcome.Message,
                consentLink = outcome.ConsentLink,
            },
            _ => PrintOutcome(outcome));

        if (outcome.Status is ConnectionOutcomeStatus.Created or ConnectionOutcomeStatus.Updated or ConnectionOutcomeStatus.Unchanged)
            return ExitSuccess;

        if (NoConsent && outcome.Status == ConnectionOutcomeStatus.ConsentRequired)
            Logger.LogInformation("Run without --no-consent to complete the sign-in automatically where the connector supports it.");

        Logger.LogError("Connection '{DisplayName}' is not usable ({Status}): {Message}",
            outcome.DisplayName, outcome.Status.ToKebabCase(), outcome.Message ?? "see the consent link");
        return ExitError;
    }

    // Text-renderer callback invoked by OutputFormatter.WriteData — OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintOutcome(ConnectionCreateOutcome outcome)
    {
        switch (outcome.Status)
        {
            case ConnectionOutcomeStatus.Unchanged:
                OutputWriter.WriteLine($"Connection '{outcome.DisplayName}' already exists for {outcome.ApiId} (id: {outcome.ConnectionId}, status: {outcome.ConnectionStatus}).");
                break;
            case ConnectionOutcomeStatus.ConsentRequired:
                OutputWriter.WriteLine($"Connection '{outcome.DisplayName}' was created (id: {outcome.ConnectionId}) but needs sign-in.");
                if (!string.IsNullOrEmpty(outcome.ConsentLink))
                    OutputWriter.WriteLine($"Open this link to complete sign-in: {outcome.ConsentLink}");
                break;
            case ConnectionOutcomeStatus.Failed:
                OutputWriter.WriteLine($"Connection '{outcome.DisplayName}' could not be created: {outcome.Message}");
                break;
            default:
                OutputWriter.WriteLine($"Connection '{outcome.DisplayName}' {outcome.Status.ToKebabCase()} (id: {outcome.ConnectionId}, connector: {outcome.ApiId}, status: {outcome.ConnectionStatus}).");
                break;
        }
    }
#pragma warning restore TXC003
}
