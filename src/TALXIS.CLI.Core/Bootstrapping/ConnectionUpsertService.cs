using System.Net;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Storage;

namespace TALXIS.CLI.Core.Bootstrapping;

/// <summary>
/// Outcome of <see cref="ConnectionUpsertService.ValidateAndUpsertAsync"/>.
/// Exceptions are reserved for unexpected store failures; input-validation
/// errors flow through <see cref="Error"/> so callers can translate to
/// their preferred exit code without parsing exception messages.
/// </summary>
public sealed record ConnectionUpsertResult(Connection? Connection, string? Error);

/// <summary>
/// Shared "validate + upsert a Dataverse connection" logic. Extracted
/// from <c>ConnectionCreateCliCommand</c> so the one-liner bootstrap
/// (<c>profile create --url</c>) writes connections through the same
/// validator — any rule that changes here changes for both entry points.
/// </summary>
public sealed class ConnectionUpsertService
{
    private readonly IConnectionStore _store;
    private readonly ILogger<ConnectionUpsertService>? _logger;

    public ConnectionUpsertService(IConnectionStore store, ILogger<ConnectionUpsertService>? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger;
    }

    /// <summary>
    /// Validates Dataverse connection inputs, normalises the URL, and
    /// upserts via <see cref="IConnectionStore"/>. Returns the persisted
    /// model on success or a user-facing error string on invalid input.
    /// </summary>
    public async Task<ConnectionUpsertResult> ValidateAndUpsertAsync(
        string name,
        ProviderKind provider,
        string? environmentUrl,
        CloudInstance? cloud,
        string? organizationId,
        string? environmentId,
        string? tenantId,
        string? description,
        CancellationToken ct,
        string? displayName = null,
        EnvironmentType? environmentType = null)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return new ConnectionUpsertResult(null, "Connection name must not be empty.");

        if (provider != ProviderKind.Dataverse)
            return new ConnectionUpsertResult(null,
                $"Provider '{provider}' is not implemented in v1. Only 'dataverse' is supported.");

        if (string.IsNullOrWhiteSpace(environmentUrl))
            return new ConnectionUpsertResult(null,
                "--environment <url> is required when --provider is 'dataverse'.");

        if (!Uri.TryCreate(environmentUrl, UriKind.Absolute, out var envUri)
            || (envUri.Scheme != Uri.UriSchemeHttp && envUri.Scheme != Uri.UriSchemeHttps))
        {
            return new ConnectionUpsertResult(null,
                $"--environment must be an absolute http(s) URL: '{environmentUrl}'.");
        }

        if (!string.IsNullOrWhiteSpace(organizationId) && !Guid.TryParse(organizationId, out _))
        {
            return new ConnectionUpsertResult(null,
                $"--organization-id must be a GUID: '{organizationId}'.");
        }

        Guid? parsedEnvironmentId = null;
        if (!string.IsNullOrWhiteSpace(environmentId))
        {
            if (!Guid.TryParse(environmentId, out var eid))
                return new ConnectionUpsertResult(null,
                    $"--environment-id must be a GUID: '{environmentId}'.");
            parsedEnvironmentId = eid;
        }

        // Non-blocking DNS reachability check — warn if the hostname cannot be resolved.
        // Let cancellation propagate so user Ctrl+C is not swallowed.
        try
        {
            await Dns.GetHostAddressesAsync(envUri.Host, ct).ConfigureAwait(false);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            _logger?.LogWarning("Hostname '{Host}' could not be resolved. The connection will be saved but may not work at runtime.", envUri.Host);
        }

        // Check if a connection already exists to preserve CreatedAt.
        var existing = await _store.GetAsync(trimmed!, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;

        var connection = new Connection
        {
            Id = trimmed!,
            Provider = provider,
            Description = description ?? displayName,
            EnvironmentUrl = envUri.ToString().TrimEnd('/'),
            Cloud = cloud ?? CloudInstance.Public,
            OrganizationId = organizationId,
            EnvironmentId = parsedEnvironmentId,
            TenantId = tenantId,
            DisplayName = displayName,
            EnvironmentType = environmentType,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now,
        };

        await _store.UpsertAsync(connection, ct).ConfigureAwait(false);
        return new ConnectionUpsertResult(connection, null);
    }
}
