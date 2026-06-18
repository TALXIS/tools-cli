using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Deployment;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control;

/// <summary>Validates deployment-settings connection references against the connections in the target environment.</summary>
public sealed class PowerPlatformConnectionValidator : IConnectionValidator
{
    private readonly IConfigurationResolver _resolver;
    private readonly IPowerPlatformEnvironmentCatalog _environments;
    private readonly IPowerPlatformConnectionCatalog _connections;
    private readonly ILogger<PowerPlatformConnectionValidator> _logger;

    public PowerPlatformConnectionValidator(
        IConfigurationResolver resolver,
        IPowerPlatformEnvironmentCatalog environments,
        IPowerPlatformConnectionCatalog connections,
        ILoggerFactory? loggerFactory = null)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _environments = environments ?? throw new ArgumentNullException(nameof(environments));
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _logger = loggerFactory?.CreateLogger<PowerPlatformConnectionValidator>()
            ?? NullLogger<PowerPlatformConnectionValidator>.Instance;
    }

    public async Task<ConnectionValidationResult> ValidateAsync(
        string? profileName,
        DeploymentSettings settings,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var references = settings.ConnectionReferences
            .Where(r => !string.IsNullOrWhiteSpace(r.ConnectionId))
            .ToList();

        if (references.Count == 0)
            return new ConnectionValidationResult(Validated: true, MissingConnections: Array.Empty<string>());

        try
        {
            var ctx = await _resolver.ResolveAsync(profileName, ct).ConfigureAwait(false);
            var environmentId = await ResolveEnvironmentIdAsync(ctx, ct).ConfigureAwait(false);

            var existing = await _connections
                .ListAsync(ctx.Connection, ctx.Credential, environmentId, ct)
                .ConfigureAwait(false);

            var missing = ConnectionMatcher.FindMissing(references, existing.Select(c => c.Id));
            return new ConnectionValidationResult(Validated: true, MissingConnections: missing);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not validate connections against the target environment; skipping pre-flight check.");
                
            return new ConnectionValidationResult(Validated: false, MissingConnections: Array.Empty<string>());
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

        var environment = await _environments
            .TryGetByEnvironmentUrlAsync(ctx.Connection, ctx.Credential, envUri, ct)
            .ConfigureAwait(false);

        if (environment is null)
            throw new InvalidOperationException(
                $"Could not resolve Power Platform environment for URL '{ctx.Connection.EnvironmentUrl}'.");

        return environment.EnvironmentId;
    }
}
