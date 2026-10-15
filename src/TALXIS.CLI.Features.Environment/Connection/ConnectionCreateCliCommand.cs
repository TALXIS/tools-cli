using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Core.Resolution;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Connection;

/// <summary>
/// <c>txc environment connection create</c> — creates the stored sign-in of a
/// connector. Works for any connector: no authentication, API key, service
/// principal, certificate, or user-delegated OAuth.
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "create",
    Description = "Create a connection for a connector in the LIVE connected environment. Requires an active profile. Idempotent by connector and display name: an existing connected connection is returned unchanged. For OAuth connectors a user credential signs in silently when the connector supports it; otherwise a consent link is returned and the command exits 1."
)]
public class ConnectionCreateCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectionCreateCliCommand));

    [CliOption(Name = "--connector", Description = "Connector logical name (for example almlab_connectorsopenfoodfacts or shared_office365users), apiId or exact display name. See 'environment connector list'.", Required = true)]
    public string Connector { get; set; } = null!;

    [CliOption(Name = "--display-name", Description = "Display name of the connection. It identifies an existing connection, so repeating the command changes nothing.", Required = true)]
    public string DisplayName { get; set; } = null!;

    [CliOption(Name = "--auth-method", Description = "Authentication method (connection parameter set), for example ServicePrincipalOauth. Defaults to the connector's only method, or its OAuth method.", Required = false)]
    public string? AuthMethod { get; set; }

    [CliOption(Name = "--parameter", Description = "Connection parameter as name=value. Repeat for several. Do not pass secrets here; use --secret-from-env.", Required = false)]
    public List<string>? Parameter { get; set; }

    [CliOption(Name = "--secret-from-env", Description = "Secret connection parameter as name=ENV_VAR; the value is read from that environment variable and never echoed. Repeat for several.", Required = false)]
    public List<string>? SecretFromEnv { get; set; }

    [CliOption(Name = "--no-consent", Description = "Do not try to complete OAuth consent; create the connection and return the consent link.", Required = false)]
    public bool NoConsent { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (!ConnectionCommandSupport.TryParsePairs(Parameter, "--parameter", out var parameters, out var error)
            || !ConnectionCommandSupport.TryParsePairs(SecretFromEnv, "--secret-from-env", out var secretSources, out error))
        {
            Logger.LogError("{Error}", error);
            return ExitValidationError;
        }

        var environment = TxcServices.Get<IEnvironmentReader>();
        foreach (var (name, variable) in secretSources)
        {
            if (parameters.ContainsKey(name))
            {
                Logger.LogError("Parameter '{Name}' was given by both --parameter and --secret-from-env.", name);
                return ExitValidationError;
            }

            var value = environment.Get(variable);
            if (string.IsNullOrEmpty(value))
            {
                Logger.LogError("Environment variable '{Variable}' for secret parameter '{Name}' is not set or empty.", variable, name);
                return ExitValidationError;
            }

            parameters[name] = value;
        }

        var service = TxcServices.Get<IConnectionService>();
        var outcome = await service.CreateAsync(
            Profile,
            new ConnectionCreateOptions(Connector, DisplayName, AuthMethod, parameters, AttemptConsent: !NoConsent),
            CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteData(
            new
            {
                status = outcome.Status,
                connectionId = outcome.ConnectionId,
                apiId = outcome.ApiId,
                displayName = outcome.DisplayName,
                connectionStatus = outcome.ConnectionStatus,
                consentLink = outcome.ConsentLink,
                message = outcome.Message,
            },
            _ => PrintOutcome(outcome));

        if (outcome.Status is "created" or "updated" or "unchanged")
            return ExitSuccess;

        Logger.LogError("Connection '{DisplayName}' is not usable ({Status}): {Message}",
            outcome.DisplayName, outcome.Status, outcome.Message ?? "see the consent link");
        return ExitError;
    }

    // Text-renderer callback invoked by OutputFormatter.WriteData — OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintOutcome(ConnectionCreateOutcome outcome)
    {
        switch (outcome.Status)
        {
            case "unchanged":
                OutputWriter.WriteLine($"Connection '{outcome.DisplayName}' already exists for {outcome.ApiId} (id: {outcome.ConnectionId}, status: {outcome.ConnectionStatus}).");
                break;
            case "consent-required":
                OutputWriter.WriteLine($"Connection '{outcome.DisplayName}' was created (id: {outcome.ConnectionId}) but needs sign-in.");
                if (!string.IsNullOrEmpty(outcome.ConsentLink))
                    OutputWriter.WriteLine($"Open this link to complete sign-in: {outcome.ConsentLink}");
                break;
            case "failed":
                OutputWriter.WriteLine($"Connection '{outcome.DisplayName}' could not be created: {outcome.Message}");
                break;
            default:
                OutputWriter.WriteLine($"Connection '{outcome.DisplayName}' {outcome.Status} (id: {outcome.ConnectionId}, connector: {outcome.ApiId}, status: {outcome.ConnectionStatus}).");
                break;
        }
    }
#pragma warning restore TXC003
}
