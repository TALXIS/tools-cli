using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Platform.PowerPlatform.Control.Graph;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;

/// <summary>
/// Connection management over the connectivity API: one implementation for
/// every connector and every authentication method, for users and service
/// principals alike.
/// </summary>
public sealed class ConnectionService : IConnectionService
{
    private const int MaxPages = 20;

    private readonly ConnectivityTargetResolver _targets;
    private readonly ConnectivityApiClient _client;
    private readonly ConnectorResolver _connectors;
    private readonly SilentConsentHelper _consent;
    private readonly MicrosoftGraphClient _graph;
    private readonly ILogger _logger;

    public ConnectionService(
        IConfigurationResolver resolver,
        IPowerPlatformEnvironmentCatalog catalog,
        IDataverseQueryService query,
        IAccessTokenService tokens,
        MicrosoftGraphClient graph,
        IHttpClientFactoryWrapper? httpFactory = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(tokens);

        _logger = loggerFactory?.CreateLogger<ConnectionService>() ?? NullLogger<ConnectionService>.Instance;
        var http = httpFactory ?? DefaultHttpClientFactoryWrapper.Instance;

        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _targets = new ConnectivityTargetResolver(resolver, catalog, _logger);
        _client = new ConnectivityApiClient(tokens, http, _logger);
        _connectors = new ConnectorResolver(_client, query, _logger);
        _consent = new SilentConsentHelper(tokens, http, _logger);
    }

    // ---------------------------------------------------------------- read

    public async Task<IReadOnlyList<ConnectionSummary>> ListAsync(
        string? profileName, string? connector, string? filter, CancellationToken ct)
    {
        var target = await _targets.ResolveAsync(profileName, ct).ConfigureAwait(false);
        string? apiId = null;
        if (!string.IsNullOrWhiteSpace(connector))
            apiId = (await _connectors.ResolveAsync(target, connector, ct).ConfigureAwait(false)).ApiId;

        var all = await ListOwnedAsync(target, ct).ConfigureAwait(false);

        return all
            .Where(c => apiId is null || string.Equals(c.ApiId, apiId, StringComparison.OrdinalIgnoreCase))
            .Where(c => string.IsNullOrWhiteSpace(filter)
                || (c.DisplayName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(c => c.ApiId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<ConnectionSummary> GetAsync(
        string? profileName, string connector, string connectionId, CancellationToken ct)
    {
        var target = await _targets.ResolveAsync(profileName, ct).ConfigureAwait(false);
        var resolved = await _connectors.ResolveAsync(target, connector, ct).ConfigureAwait(false);
        var element = await GetConnectionElementAsync(target, resolved.ApiId, connectionId, ct).ConfigureAwait(false);
        return ParseConnection(element.RootElement, resolved.ApiId);
    }

    public async Task<ConnectionTestOutcome> TestAsync(
        string? profileName, string connector, string connectionId, CancellationToken ct)
    {
        var target = await _targets.ResolveAsync(profileName, ct).ConfigureAwait(false);
        var resolved = await _connectors.ResolveAsync(target, connector, ct).ConfigureAwait(false);
        using var element = await GetConnectionElementAsync(target, resolved.ApiId, connectionId, ct).ConfigureAwait(false);
        var summary = ParseConnection(element.RootElement, resolved.ApiId);

        if (!string.Equals(summary.ConnectionStatus, "Connected", StringComparison.OrdinalIgnoreCase))
            return new ConnectionTestOutcome(false, summary.ConnectionStatus, summary.StatusMessage);

        // Connectors that expose a test link get a live call; the rest are
        // healthy when the platform says they are connected.
        var testLink = TestLinkOf(element.RootElement);
        if (testLink is null)
            return new ConnectionTestOutcome(true, summary.ConnectionStatus, null);

        var (healthy, message) = await _consent.RunTestLinkAsync(target.Connection, target.Credential, testLink, ct)
            .ConfigureAwait(false);
        return new ConnectionTestOutcome(healthy, summary.ConnectionStatus, message);
    }

    // -------------------------------------------------------------- create

    public async Task<ConnectionCreateOutcome> CreateAsync(
        string? profileName, ConnectionCreateOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.DisplayName))
            throw new ArgumentException("A display name is required.", nameof(options));

        var target = await _targets.ResolveAsync(profileName, ct).ConfigureAwait(false);
        var connector = await _connectors.ResolveAsync(target, options.Connector, ct).ConfigureAwait(false);

        // Idempotency: a connection with this display name for this connector.
        var existing = (await ListOwnedAsync(target, ct).ConfigureAwait(false))
            .Where(c => string.Equals(c.ApiId, connector.ApiId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(c.DisplayName, options.DisplayName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (existing.Count > 1)
        {
            throw new ArgumentException(
                $"{existing.Count} connections named '{options.DisplayName}' already exist for connector '{connector.LogicalName}': " +
                string.Join(", ", existing.Select(c => c.ConnectionId)) + ". Use a unique display name.");
        }

        if (existing.Count == 1 && string.Equals(existing[0].ConnectionStatus, "Connected", StringComparison.OrdinalIgnoreCase))
        {
            return new ConnectionCreateOutcome(
                "unchanged", existing[0].ConnectionId, connector.ApiId, options.DisplayName,
                existing[0].ConnectionStatus, null, null);
        }

        var reuse = existing.Count == 1;
        var connectionId = reuse ? existing[0].ConnectionId : Guid.NewGuid().ToString();

        var method = await SelectMethodAsync(target, connector, options, ct).ConfigureAwait(false);
        ConnectionRequestBuilder.Validate(method, options.Parameters, connector.LogicalName);

        var wantsConsentLink = method.IsInteractiveOAuth;
        var body = ConnectionRequestBuilder.BuildBody(
            options.DisplayName, target.EnvironmentId, method, options.Parameters, wantsConsentLink);

        using var put = await _client.SendAsync(
            HttpMethod.Put,
            ConnectivityEndpoints.Connection(target.BaseUri, connector.ApiId, connectionId, wantsConsentLink),
            target.Connection, target.Credential, body, ct).ConfigureAwait(false);

        var root = put.Body?.RootElement ?? default;
        var state = ReadState(root);
        var verb = reuse ? "updated" : "created";

        if (state.Error is null)
            return Outcome(verb, connectionId, connector, options, state.Status, null, null);

        if (!state.IsUnauthenticated)
        {
            // The platform rejected the credentials (bad secret, unknown app, ...).
            // Do not leave a broken connection behind that this run created.
            if (!reuse)
                await TryDeleteAsync(target, connector.ApiId, connectionId, ct).ConfigureAwait(false);

            return Outcome("failed", connectionId, connector, options, state.Status, null, state.Error);
        }

        // Consent is pending. Complete it silently when that is possible.
        var consentInfo = ConsentInfoOf(root);
        if (options.AttemptConsent && consentInfo.FirstPartyLoginUri is not null)
        {
            var attempt = await _consent.TryCompleteAsync(
                target.Connection, target.Credential, consentInfo.FirstPartyLoginUri, ct).ConfigureAwait(false);

            if (attempt.Completed)
            {
                using var refreshed = await GetConnectionElementAsync(target, connector.ApiId, connectionId, ct).ConfigureAwait(false);
                var refreshedState = ReadState(refreshed.RootElement);
                if (refreshedState.Error is null)
                    return Outcome(verb, connectionId, connector, options, refreshedState.Status, null, null);

                return Outcome("consent-required", connectionId, connector, options, refreshedState.Status,
                    consentInfo.ConsentLink, refreshedState.Error);
            }

            return Outcome("consent-required", connectionId, connector, options, state.Status,
                consentInfo.ConsentLink, attempt.Failure);
        }

        return Outcome("consent-required", connectionId, connector, options, state.Status, consentInfo.ConsentLink,
            consentInfo.FirstPartyLoginUri is null
                ? "This connector needs a person to sign in; open the consent link."
                : "Consent was not attempted; open the consent link or run without --no-consent.");
    }

    private async Task<AuthMethod> SelectMethodAsync(
        ConnectivityTarget target, ResolvedConnector connector, ConnectionCreateOptions options, CancellationToken ct)
    {
        using var response = await _client.SendAsync(
            HttpMethod.Get,
            ConnectivityEndpoints.Connector(target.BaseUri, connector.ApiId, target.EnvironmentId),
            target.Connection, target.Credential, null, ct).ConfigureAwait(false);

        var methods = response.Body is { } doc
            ? ConnectorAuthMethodReader.Read(doc.RootElement)
            : [new AuthMethod(ConnectorAuthMethodReader.NoAuthMethodName, null, false, [])];

        return ConnectionRequestBuilder.Select(
            methods, options.AuthMethod, options.Parameters.Keys.ToList(), connector.LogicalName);
    }

    // -------------------------------------------------------------- delete

    public async Task DeleteAsync(string? profileName, string connector, string connectionId, CancellationToken ct)
    {
        var target = await _targets.ResolveAsync(profileName, ct).ConfigureAwait(false);
        var resolved = await _connectors.ResolveAsync(target, connector, ct).ConfigureAwait(false);

        using var _ = await _client.SendAsync(
            HttpMethod.Delete,
            ConnectivityEndpoints.ConnectionInEnvironment(target.BaseUri, resolved.ApiId, connectionId, target.EnvironmentId),
            target.Connection, target.Credential, null, ct).ConfigureAwait(false);
    }

    private async Task TryDeleteAsync(ConnectivityTarget target, string apiId, string connectionId, CancellationToken ct)
    {
        try
        {
            using var _ = await _client.SendAsync(
                HttpMethod.Delete,
                ConnectivityEndpoints.ConnectionInEnvironment(target.BaseUri, apiId, connectionId, target.EnvironmentId),
                target.Connection, target.Credential, null, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not remove the connection {ConnectionId} after a failed create.", connectionId);
        }
    }

    // --------------------------------------------------------- permissions

    public async Task<IReadOnlyList<ConnectionPermission>> ListPermissionsAsync(
        string? profileName, string connector, string connectionId, CancellationToken ct)
    {
        var target = await _targets.ResolveAsync(profileName, ct).ConfigureAwait(false);
        var resolved = await _connectors.ResolveAsync(target, connector, ct).ConfigureAwait(false);
        return await FetchPermissionsAsync(target, resolved.ApiId, connectionId, ct).ConfigureAwait(false);
    }

    public async Task<ConnectionPermissionOutcome> AddPermissionAsync(
        string? profileName,
        string connector,
        string connectionId,
        ConnectionPrincipalSpec principal,
        ConnectionPermissionRole role,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var target = await _targets.ResolveAsync(profileName, ct).ConfigureAwait(false);
        var resolved = await _connectors.ResolveAsync(target, connector, ct).ConfigureAwait(false);
        var who = await ResolvePrincipalAsync(target, principal, ct).ConfigureAwait(false);

        var current = await FetchPermissionsAsync(target, resolved.ApiId, connectionId, ct).ConfigureAwait(false);
        var existing = current.FirstOrDefault(p => who.Matches(p));
        var wanted = ConnectionPermissionMapper.ToPlatformRole(role);

        if (existing is not null && string.Equals(existing.Role, ConnectionPermissionMapper.OwnerRole, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"{who.Description} owns this connection; the owner's access cannot be changed.");

        if (existing is not null && string.Equals(existing.Role, wanted, StringComparison.OrdinalIgnoreCase))
            return new ConnectionPermissionOutcome("unchanged", connectionId, resolved.ApiId, existing);

        var request = ConnectionPermissionMapper.BuildPut(who.Id, who.Email, who.PlatformType, target.Connection.TenantId, wanted);
        using var _ = await _client.SendAsync(
            HttpMethod.Post,
            ConnectivityEndpoints.ModifyPermissions(target.BaseUri, resolved.ApiId, connectionId, target.EnvironmentId),
            target.Connection, target.Credential, request, ct).ConfigureAwait(false);

        var after = await FetchPermissionsAsync(target, resolved.ApiId, connectionId, ct).ConfigureAwait(false);
        var granted = after.FirstOrDefault(p => who.Matches(p));
        return new ConnectionPermissionOutcome(existing is null ? "assigned" : "updated", connectionId, resolved.ApiId, granted);
    }

    public async Task<ConnectionPermissionOutcome> RemovePermissionAsync(
        string? profileName,
        string connector,
        string connectionId,
        ConnectionPrincipalSpec principal,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var target = await _targets.ResolveAsync(profileName, ct).ConfigureAwait(false);
        var resolved = await _connectors.ResolveAsync(target, connector, ct).ConfigureAwait(false);
        var who = await ResolvePrincipalAsync(target, principal, ct).ConfigureAwait(false);

        var current = await FetchPermissionsAsync(target, resolved.ApiId, connectionId, ct).ConfigureAwait(false);
        var existing = current.FirstOrDefault(p => who.Matches(p));
        if (existing is null)
            return new ConnectionPermissionOutcome("unchanged", connectionId, resolved.ApiId, null);

        // The platform enforces this too, but a clear message beats its 403.
        if (string.Equals(existing.Role, ConnectionPermissionMapper.OwnerRole, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"{who.Description} owns this connection; the owner cannot be removed.");

        var request = ConnectionPermissionMapper.BuildDelete(resolved.ApiId, connectionId, existing.PermissionId);
        using var _ = await _client.SendAsync(
            HttpMethod.Post,
            ConnectivityEndpoints.ModifyPermissions(target.BaseUri, resolved.ApiId, connectionId, target.EnvironmentId),
            target.Connection, target.Credential, request, ct).ConfigureAwait(false);

        return new ConnectionPermissionOutcome("removed", connectionId, resolved.ApiId, existing);
    }

    private async Task<IReadOnlyList<ConnectionPermission>> FetchPermissionsAsync(
        ConnectivityTarget target, string apiId, string connectionId, CancellationToken ct)
    {
        using var response = await _client.SendAsync(
            HttpMethod.Get,
            ConnectivityEndpoints.Permissions(target.BaseUri, apiId, connectionId, target.EnvironmentId),
            target.Connection, target.Credential, null, ct).ConfigureAwait(false);

        return response.Body is { } doc ? ConnectionPermissionMapper.ParseList(doc.RootElement) : [];
    }

    private async Task<ResolvedPrincipal> ResolvePrincipalAsync(
        ConnectivityTarget target, ConnectionPrincipalSpec spec, CancellationToken ct)
    {
        var text = spec.Identifier?.Trim();
        if (string.IsNullOrEmpty(text))
            throw new ArgumentException("A principal identifier is required.");

        switch (spec.Kind)
        {
            case ConnectionPrincipalKind.User:
                if (Guid.TryParse(text, out var userId))
                    return new ResolvedPrincipal(userId.ToString(), null, "User", $"User {text}");
                if (text.Contains('@', StringComparison.Ordinal))
                    return new ResolvedPrincipal(null, text, "User", $"User {text}");
                throw new ArgumentException($"'{text}' is neither a user principal name nor an Entra object id.");

            case ConnectionPrincipalKind.Group:
                if (!Guid.TryParse(text, out var groupId))
                    throw new ArgumentException($"'{text}' is not an Entra group object id.");
                return new ResolvedPrincipal(groupId.ToString(), null, "Group", $"Group {text}");

            case ConnectionPrincipalKind.ServicePrincipal:
                if (!Guid.TryParse(text, out var spId))
                    throw new ArgumentException($"'{text}' is neither a client id nor an Entra object id.");
                return await ResolveServicePrincipalAsync(target, spId, ct).ConfigureAwait(false);

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
                return new ResolvedPrincipal(byApp[0].Id.ToString(), null, "ServicePrincipal",
                    $"Service principal {byApp[0].DisplayName ?? identifier.ToString()}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Could not resolve service principal '{Identifier}' through Graph; using it as an object id.", identifier);
        }

        return new ResolvedPrincipal(identifier.ToString(), null, "ServicePrincipal", $"Service principal {identifier}");
    }

    // ------------------------------------------------------------- helpers

    private async Task<IReadOnlyList<ConnectionSummary>> ListOwnedAsync(ConnectivityTarget target, CancellationToken ct)
    {
        var callerId = await _client.GetCallerObjectIdAsync(target.Connection, target.Credential, ct).ConfigureAwait(false);
        var uri = ConnectivityEndpoints.Connections(target.BaseUri, target.EnvironmentId, callerId);
        var result = new List<ConnectionSummary>();

        for (var page = 0; page < MaxPages && uri is not null; page++)
        {
            using var response = await _client.SendAsync(
                HttpMethod.Get, uri, target.Connection, target.Credential, null, ct).ConfigureAwait(false);
            if (response.Body is not { } doc)
                break;

            var root = doc.RootElement;
            if (root.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                {
                    var apiId = ApiIdOf(item);
                    if (!string.IsNullOrEmpty(apiId))
                        result.Add(ParseConnection(item, apiId));
                }
            }

            uri = root.TryGetProperty("nextLink", out var next)
                && next.ValueKind == JsonValueKind.String
                && Uri.TryCreate(next.GetString(), UriKind.Absolute, out var nextUri)
                    ? nextUri
                    : null;
        }

        return result;
    }

    private async Task<JsonDocument> GetConnectionElementAsync(
        ConnectivityTarget target, string apiId, string connectionId, CancellationToken ct)
    {
        var response = await _client.SendAsync(
            HttpMethod.Get,
            ConnectivityEndpoints.ConnectionInEnvironment(target.BaseUri, apiId, connectionId, target.EnvironmentId),
            target.Connection, target.Credential, null, ct).ConfigureAwait(false);

        return response.Body
            ?? throw new InvalidOperationException($"The platform returned no body for connection '{connectionId}'.");
    }

    private static ConnectionCreateOutcome Outcome(
        string status, string connectionId, ResolvedConnector connector, ConnectionCreateOptions options,
        string? connectionStatus, string? consentLink, string? message)
        => new(status, connectionId, connector.ApiId, options.DisplayName, connectionStatus, consentLink, message);

    private static string? ApiIdOf(JsonElement connection)
    {
        if (connection.ValueKind != JsonValueKind.Object
            || !connection.TryGetProperty("properties", out var props)
            || props.ValueKind != JsonValueKind.Object
            || !props.TryGetProperty("apiId", out var api)
            || api.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return ConnectivityEndpoints.ToShortApiId(api.GetString()!);
    }

    internal static ConnectionSummary ParseConnection(JsonElement connection, string apiId)
    {
        var props = connection.TryGetProperty("properties", out var p) ? p : default;
        var state = ReadState(connection);

        string? createdByType = null;
        string? createdBy = null;
        if (props.ValueKind == JsonValueKind.Object
            && props.TryGetProperty("createdBy", out var by)
            && by.ValueKind == JsonValueKind.Object)
        {
            createdByType = Str(by, "type");
            createdBy = Str(by, "userPrincipalName") ?? Str(by, "displayName") ?? Str(by, "id");
        }

        DateTimeOffset? created = null;
        if (Str(props, "createdTime") is { } time
            && DateTimeOffset.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            created = parsed;
        }

        return new ConnectionSummary(
            Str(connection, "name") ?? string.Empty,
            apiId,
            Str(props, "displayName"),
            state.Status,
            state.Error,
            createdByType,
            createdBy,
            created);
    }

    /// <summary>
    /// The connection's platform state: <c>Connected</c>, or the first error with
    /// its code. <see cref="ConnectionState.Error"/> is null when healthy.
    /// </summary>
    internal static ConnectionState ReadState(JsonElement connection)
    {
        if (connection.ValueKind != JsonValueKind.Object
            || !connection.TryGetProperty("properties", out var props)
            || props.ValueKind != JsonValueKind.Object
            || !props.TryGetProperty("statuses", out var statuses)
            || statuses.ValueKind != JsonValueKind.Array)
        {
            return new ConnectionState(null, null, null);
        }

        string? status = null;
        foreach (var entry in statuses.EnumerateArray())
        {
            status ??= Str(entry, "status");
            if (entry.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                return new ConnectionState(
                    Str(entry, "status") ?? "Error",
                    Str(error, "code"),
                    Str(error, "message") ?? Str(error, "code") ?? "The connection is in an error state.");
            }
        }

        return new ConnectionState(status, null, null);
    }

    private static (Uri? FirstPartyLoginUri, string? ConsentLink) ConsentInfoOf(JsonElement connection)
    {
        if (connection.ValueKind != JsonValueKind.Object
            || !connection.TryGetProperty("properties", out var props)
            || props.ValueKind != JsonValueKind.Object
            || !props.TryGetProperty("consentInfo", out var info)
            || info.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }

        Uri? login = Str(info, "firstPartyLoginUri") is { } text && Uri.TryCreate(text, UriKind.Absolute, out var uri) ? uri : null;
        return (login, Str(info, "consentLink"));
    }

    private static Uri? TestLinkOf(JsonElement connection)
    {
        if (connection.ValueKind == JsonValueKind.Object
            && connection.TryGetProperty("properties", out var props)
            && props.TryGetProperty("testLinks", out var links)
            && links.ValueKind == JsonValueKind.Array
            && links.GetArrayLength() > 0
            && Str(links[0], "requestUri") is { } text
            && Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return uri;
        }

        return null;
    }

    private static string? Str(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;

    internal sealed record ConnectionState(string? Status, string? ErrorCode, string? Error)
    {
        public bool IsUnauthenticated => string.Equals(ErrorCode, "Unauthenticated", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ResolvedPrincipal(string? Id, string? Email, string PlatformType, string Description)
    {
        public bool Matches(ConnectionPermission permission)
            => (Id is not null && string.Equals(permission.PrincipalId, Id, StringComparison.OrdinalIgnoreCase))
               || (Email is not null && string.Equals(permission.Email, Email, StringComparison.OrdinalIgnoreCase));
    }
}
