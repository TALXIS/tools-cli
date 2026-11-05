using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Platform.PowerPlatform.Control.Graph;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;

/// <summary>A principal in the form a connection permission names it.</summary>
/// <param name="Id">Entra object id, or null when the principal is addressed by e-mail.</param>
internal sealed record ResolvedPrincipal(string? Id, string? Email, ConnectionPrincipalKind Kind, string Description)
{
    /// <summary>The platform names principal types exactly as <see cref="ConnectionPrincipalKind"/> does.</summary>
    public string PlatformType => Kind.ToString();

    public bool Matches(ConnectionPermission permission)
        => (Id is not null && string.Equals(permission.PrincipalId, Id, StringComparison.OrdinalIgnoreCase))
           || (Email is not null && string.Equals(permission.Email, Email, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Turns what a person typed for a principal into the form a connection
/// permission is keyed by.
/// </summary>
internal sealed class PrincipalResolver
{
    private readonly MicrosoftGraphClient _graph;
    private readonly ILogger _logger;

    public PrincipalResolver(MicrosoftGraphClient graph, ILogger<PrincipalResolver>? logger = null)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _logger = logger ?? NullLogger<PrincipalResolver>.Instance;
    }

    /// <exception cref="ArgumentException">The identifier is not valid for the kind of principal.</exception>
    public async Task<ResolvedPrincipal> ResolveAsync(
        ConnectivityTarget target, ConnectionPrincipalSpec spec, CancellationToken ct)
    {
        var text = spec.Identifier?.Trim();
        if (string.IsNullOrEmpty(text))
            throw new ArgumentException("A principal identifier is required.");

        switch (spec.Kind)
        {
            case ConnectionPrincipalKind.User:
                if (Guid.TryParse(text, out var userId))
                    return new ResolvedPrincipal(userId.ToString(), null, ConnectionPrincipalKind.User, $"User {text}");
                if (text.Contains('@', StringComparison.Ordinal))
                    return new ResolvedPrincipal(null, text, ConnectionPrincipalKind.User, $"User {text}");
                throw new ArgumentException($"'{text}' is neither a user principal name nor an Entra object id.");

            case ConnectionPrincipalKind.Group:
                if (!Guid.TryParse(text, out var groupId))
                    throw new ArgumentException($"'{text}' is not an Entra group object id.");
                return new ResolvedPrincipal(groupId.ToString(), null, ConnectionPrincipalKind.Group, $"Group {text}");

            case ConnectionPrincipalKind.ServicePrincipal:
                if (!Guid.TryParse(text, out var servicePrincipalId))
                    throw new ArgumentException($"'{text}' is neither a client id nor an Entra object id.");
                return await ResolveServicePrincipalAsync(target, servicePrincipalId, ct).ConfigureAwait(false);

            default:
                throw new ArgumentOutOfRangeException(nameof(spec), spec.Kind, "Unknown principal kind.");
        }
    }

    /// <summary>
    /// A GUID may be the application (client) id or the object id. The
    /// connection permission is keyed by the object id, so look the client id
    /// up; when the directory cannot be read, treat the value as the object id.
    /// </summary>
    private async Task<ResolvedPrincipal> ResolveServicePrincipalAsync(
        ConnectivityTarget target, Guid identifier, CancellationToken ct)
    {
        try
        {
            var byApp = await _graph.ListServicePrincipalsAsync(
                target.Connection, target.Credential, $"appId eq '{identifier}'", 2, ct).ConfigureAwait(false);
            if (byApp.Count == 1)
            {
                return new ResolvedPrincipal(byApp[0].Id.ToString(), null, ConnectionPrincipalKind.ServicePrincipal,
                    $"Service principal {byApp[0].DisplayName ?? identifier.ToString()}");
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex, "Could not resolve service principal '{Identifier}' through Graph; using it as an object id.", identifier);
        }

        return new ResolvedPrincipal(identifier.ToString(), null, ConnectionPrincipalKind.ServicePrincipal,
            $"Service principal {identifier}");
    }
}
