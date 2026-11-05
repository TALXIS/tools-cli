using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;

namespace TALXIS.CLI.Platform.PowerPlatform.Control;

/// <summary>
/// The stored environment id wins; otherwise the environment is looked up in
/// the platform catalog from the Dataverse URL.
/// </summary>
internal sealed class EnvironmentIdResolver : IEnvironmentIdResolver
{
    private readonly IPowerPlatformEnvironmentCatalog _catalog;
    private readonly ILogger _logger;

    public EnvironmentIdResolver(
        IPowerPlatformEnvironmentCatalog catalog,
        ILogger<EnvironmentIdResolver>? logger = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _logger = logger ?? NullLogger<EnvironmentIdResolver>.Instance;
    }

    public async Task<Guid> ResolveAsync(Connection connection, Credential credential, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(credential);

        if (connection.EnvironmentId.HasValue)
            return connection.EnvironmentId.Value;

        if (string.IsNullOrWhiteSpace(connection.EnvironmentUrl)
            || !Uri.TryCreate(connection.EnvironmentUrl, UriKind.Absolute, out var environmentUrl))
        {
            throw new InvalidOperationException(
                $"Connection '{connection.Id}' has no EnvironmentId and no usable EnvironmentUrl to resolve one from.");
        }

        _logger.LogDebug(
            "EnvironmentId not stored on connection '{ConnectionId}'. Resolving via the Power Platform catalog...",
            connection.Id);

        var environment = await LookUpAsync(connection, credential, environmentUrl, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Could not resolve a Power Platform environment for '{environmentUrl}'. " +
                "Set the environment ID on the connection, or re-run the connection live check.");

        return environment.EnvironmentId;
    }

    // Listing environments needs Power Platform admin rights, which an application
    // identity usually lacks; say how to get past that instead of just the 403.
    private async Task<PowerPlatformEnvironmentSummary?> LookUpAsync(
        Connection connection, Credential credential, Uri environmentUrl, CancellationToken ct)
    {
        try
        {
            return await _catalog
                .TryGetByEnvironmentUrlAsync(connection, credential, environmentUrl, ct)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (!SilentConsentHelper.IsUserCredential(credential))
        {
            throw new InvalidOperationException(
                ex.Message + " An application credential usually cannot list environments: store the environment ID " +
                "on the connection by running its live check once with a user credential.", ex);
        }
    }
}
