using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Platforms.PowerPlatform;


namespace TALXIS.CLI.Platform.PowerPlatform.Control;

/// <summary>
/// Unified orchestrator that queries all settings backends (control plane,
/// Organization table, solution settings, copilot governance) and presents
/// a single flat key-value view. The CLI consumer never sees individual
/// backends — this service handles routing and merging transparently.
/// </summary>
public sealed class EnvironmentSettingsService : IEnvironmentSettingsService
{
    private readonly IConfigurationResolver _resolver;
    private readonly IPowerPlatformEnvironmentCatalog _catalog;
    private readonly ISettingsBackend[] _backends;
    private readonly ILogger<EnvironmentSettingsService> _logger;

    public EnvironmentSettingsService(
        IConfigurationResolver resolver,
        IPowerPlatformEnvironmentCatalog catalog,
        EnvironmentSettingsClient controlPlaneClient,
        IAccessTokenService tokens,
        IHttpClientFactoryWrapper? httpFactory = null,
        ILoggerFactory? loggerFactory = null)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _logger = loggerFactory?.CreateLogger<EnvironmentSettingsService>()
            ?? NullLogger<EnvironmentSettingsService>.Instance;

        // Backends in priority order for update routing.
        _backends = new ISettingsBackend[]
        {
            new ControlPlaneSettingsBackend(controlPlaneClient),
            new CopilotGovernanceSettingsBackend(tokens, httpFactory,
                loggerFactory?.CreateLogger<CopilotGovernanceSettingsBackend>()),
            new OrganizationTableSettingsBackend(tokens, httpFactory),
            new SolutionSettingsBackend(tokens, httpFactory),
        };
    }

    public async Task<IReadOnlyList<EnvironmentSetting>> ListAsync(
        string? profileName, CancellationToken ct)
    {
        var ctx = await _resolver.ResolveAsync(profileName, ct).ConfigureAwait(false);
        var envId = await ResolveEnvironmentIdAsync(ctx, ct).ConfigureAwait(false);

        _logger.LogDebug("Listing environment settings from all backends for environment {EnvironmentId}.", envId);

        // Query all backends in parallel.
        var tasks = _backends.Select(b => SafeListAsync(b, ctx.Connection, ctx.Credential, envId, ct));
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        // Merge and deduplicate (first backend wins on name collision).
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<EnvironmentSetting>();

        foreach (var backendResults in results)
        {
            foreach (var setting in backendResults)
            {
                if (seen.Add(setting.Name))
                    merged.Add(setting);
            }
        }

        return merged;
    }

    public async Task UpdateAsync(
        string? profileName, string settingName, string value, CancellationToken ct)
    {
        var ctx = await _resolver.ResolveAsync(profileName, ct).ConfigureAwait(false);
        var envId = await ResolveEnvironmentIdAsync(ctx, ct).ConfigureAwait(false);

        _logger.LogDebug(
            "Updating setting '{Setting}' to '{Value}' for environment {EnvironmentId}.",
            settingName, value, envId);

        // Try each backend in priority order until one handles the setting.
        foreach (var backend in _backends)
        {
            if (await backend.TryUpdateAsync(ctx.Connection, ctx.Credential, envId, settingName, value, ct)
                    .ConfigureAwait(false))
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"Setting '{settingName}' was not recognized by any backend. " +
            "Verify the setting name is correct (names are case-sensitive for some backends).");
    }

    /// <summary>
    /// Wraps a backend's ListAsync in error handling so a failing backend
    /// doesn't prevent other backends from returning their settings.
    /// </summary>
    private async Task<IReadOnlyList<EnvironmentSetting>> SafeListAsync(
        ISettingsBackend backend, Connection connection, Credential credential,
        Guid environmentId, CancellationToken ct)
    {
        try
        {
            return await backend.ListAsync(connection, credential, environmentId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Settings backend {Backend} failed during list. Skipping.",
                backend.GetType().Name);
            return Array.Empty<EnvironmentSetting>();
        }
    }

    private async Task<Guid> ResolveEnvironmentIdAsync(ResolvedProfileContext ctx, CancellationToken ct)
    {
        if (ctx.Connection.EnvironmentId.HasValue)
            return ctx.Connection.EnvironmentId.Value;

        if (string.IsNullOrWhiteSpace(ctx.Connection.EnvironmentUrl)
            || !Uri.TryCreate(ctx.Connection.EnvironmentUrl, UriKind.Absolute, out var envUri))
            throw new InvalidOperationException(
                $"Connection '{ctx.Connection.Id}' has no EnvironmentUrl or EnvironmentId.");

        _logger.LogInformation(
            "EnvironmentId not stored on connection '{ConnectionId}'. Resolving via Power Platform catalog...",
            ctx.Connection.Id);

        var env = await _catalog
            .TryGetByEnvironmentUrlAsync(ctx.Connection, ctx.Credential, envUri, ct)
            .ConfigureAwait(false);

        if (env is null)
            throw new InvalidOperationException(
                $"Could not resolve Power Platform environment for URL '{ctx.Connection.EnvironmentUrl}'.");

        _logger.LogInformation("Resolved EnvironmentId {EnvironmentId}.", env.EnvironmentId);
        return env.EnvironmentId;
    }
}
