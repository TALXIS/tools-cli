using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control;

/// <summary>
/// Implements <see cref="IEnvironmentManagementSettingsService"/> by resolving
/// the caller's profile, looking up (or lazily resolving) the environment
/// GUID, and delegating to <see cref="EnvironmentManagementSettingsClient"/>.
/// </summary>
public sealed class EnvironmentManagementSettingsService : IEnvironmentManagementSettingsService
{
    private readonly IConfigurationResolver _resolver;
    private readonly EnvironmentManagementSettingsClient _client;
    private readonly IPowerPlatformEnvironmentCatalog _catalog;
    private readonly ILogger<EnvironmentManagementSettingsService> _logger;

    public EnvironmentManagementSettingsService(
        IConfigurationResolver resolver,
        EnvironmentManagementSettingsClient client,
        IPowerPlatformEnvironmentCatalog catalog,
        ILogger<EnvironmentManagementSettingsService>? logger = null)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _logger = logger ?? NullLogger<EnvironmentManagementSettingsService>.Instance;
    }

    public async Task<IReadOnlyList<EnvironmentManagementSetting>> ListAsync(
        string? profileName, string? selectFilter, CancellationToken ct)
    {
        var ctx = await _resolver.ResolveAsync(profileName, ct).ConfigureAwait(false);
        var envId = await ResolveEnvironmentIdAsync(ctx, ct).ConfigureAwait(false);

        _logger.LogDebug("Listing environment management settings for environment {EnvironmentId}.", envId);
        return await _client.ListAsync(ctx.Connection, ctx.Credential, envId, selectFilter, ct)
            .ConfigureAwait(false);
    }

    public async Task UpdateAsync(
        string? profileName, string settingName, string value, CancellationToken ct)
    {
        var ctx = await _resolver.ResolveAsync(profileName, ct).ConfigureAwait(false);
        var envId = await ResolveEnvironmentIdAsync(ctx, ct).ConfigureAwait(false);

        _logger.LogDebug(
            "Updating environment management setting '{Setting}' to '{Value}' for environment {EnvironmentId}.",
            settingName, value, envId);
        await _client.UpdateAsync(ctx.Connection, ctx.Credential, envId, settingName, value, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the environment GUID from the connection if stored, otherwise
    /// resolves it via <see cref="IPowerPlatformEnvironmentCatalog"/> using
    /// the environment URL.
    /// </summary>
    private async Task<Guid> ResolveEnvironmentIdAsync(ResolvedProfileContext ctx, CancellationToken ct)
    {
        if (ctx.Connection.EnvironmentId.HasValue)
            return ctx.Connection.EnvironmentId.Value;

        if (string.IsNullOrWhiteSpace(ctx.Connection.EnvironmentUrl))
            throw new InvalidOperationException(
                $"Connection '{ctx.Connection.Id}' has no EnvironmentUrl or EnvironmentId. " +
                "Cannot resolve the Power Platform environment for the control plane API.");

        if (!Uri.TryCreate(ctx.Connection.EnvironmentUrl, UriKind.Absolute, out var envUri))
            throw new InvalidOperationException(
                $"Connection '{ctx.Connection.Id}' EnvironmentUrl '{ctx.Connection.EnvironmentUrl}' is not a valid URI.");

        _logger.LogInformation(
            "EnvironmentId not stored on connection '{ConnectionId}'. Resolving via Power Platform catalog...",
            ctx.Connection.Id);

        var env = await _catalog
            .TryGetByEnvironmentUrlAsync(ctx.Connection, ctx.Credential, envUri, ct)
            .ConfigureAwait(false);

        if (env is null)
            throw new InvalidOperationException(
                $"Could not resolve Power Platform environment for URL '{ctx.Connection.EnvironmentUrl}'. " +
                "Set --environment-id on the connection or verify the URL.");

        _logger.LogInformation(
            "Resolved EnvironmentId {EnvironmentId} for '{EnvironmentUrl}'. " +
            "Consider running 'txc config connection create' with --environment-id to avoid this lookup.",
            env.EnvironmentId, ctx.Connection.EnvironmentUrl);

        return env.EnvironmentId;
    }
}
