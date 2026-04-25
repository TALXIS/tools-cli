using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control;

namespace TALXIS.CLI.Platform.Dataverse.Runtime;

/// <summary>
/// Resolves and persists the Power Platform environment type by querying the
/// admin API catalog. Called by <c>profile validate --refresh-env-type</c>.
/// </summary>
public sealed class DataverseEnvironmentTypeResolver : IEnvironmentTypeResolver
{
    private readonly IPowerPlatformEnvironmentCatalog _catalog;
    private readonly IConnectionStore _connectionStore;
    private readonly ILogger<DataverseEnvironmentTypeResolver> _logger;

    public DataverseEnvironmentTypeResolver(
        IPowerPlatformEnvironmentCatalog catalog,
        IConnectionStore connectionStore,
        ILogger<DataverseEnvironmentTypeResolver>? logger = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _connectionStore = connectionStore ?? throw new ArgumentNullException(nameof(connectionStore));
        _logger = logger ?? NullLogger<DataverseEnvironmentTypeResolver>.Instance;
    }

    public async Task<EnvironmentType?> RefreshAsync(Connection connection, Credential credential, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(credential);

        if (string.IsNullOrWhiteSpace(connection.EnvironmentUrl) ||
            !Uri.TryCreate(connection.EnvironmentUrl, UriKind.Absolute, out var envUri))
        {
            _logger.LogWarning("Cannot refresh environment type — connection '{Id}' has no valid environment URL.", connection.Id);
            return connection.EnvironmentType;
        }

        var environment = await _catalog.TryGetByEnvironmentUrlAsync(connection, credential, envUri, ct).ConfigureAwait(false);
        if (environment is null)
        {
            _logger.LogWarning("Environment not found in Power Platform catalog for '{Url}'.", connection.EnvironmentUrl);
            return connection.EnvironmentType;
        }

        var newType = environment.EnvironmentType;
        var changed = newType != connection.EnvironmentType
            || !string.Equals(environment.DisplayName, connection.DisplayName, StringComparison.Ordinal);

        if (changed)
        {
            _logger.LogInformation(
                "Refreshed connection '{ConnectionId}': type {OldType} → {NewType}, displayName='{DisplayName}'.",
                connection.Id,
                connection.EnvironmentType?.ToString() ?? "(null)",
                newType?.ToString() ?? "(null)",
                environment.DisplayName);

            // Only overwrite EnvironmentType when the API returned a value;
            // a null SKU should not clear a previously-set type.
            if (newType is not null)
                connection.EnvironmentType = newType;
            connection.DisplayName = environment.DisplayName;
            if (environment.OrganizationId is { } orgId)
                connection.OrganizationId = orgId.ToString();
            connection.EnvironmentId = environment.EnvironmentId;
            connection.UpdatedAt = DateTimeOffset.UtcNow;
            await _connectionStore.UpsertAsync(connection, ct).ConfigureAwait(false);
        }

        return newType ?? connection.EnvironmentType;
    }
}
