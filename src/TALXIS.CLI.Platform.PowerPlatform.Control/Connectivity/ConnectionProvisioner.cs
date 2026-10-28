using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;

/// <summary>
/// Creates and deletes connections. A create is idempotent by connector and
/// display name, and rolls back a connection the platform rejected.
/// </summary>
internal sealed class ConnectionProvisioner
{
    private readonly ConnectivityTargetResolver _targets;
    private readonly ConnectivityApiClient _client;
    private readonly ConnectorResolver _connectors;
    private readonly ConnectionReader _reader;
    private readonly ConnectionConsentFlow _consent;
    private readonly ILogger _logger;

    public ConnectionProvisioner(
        ConnectivityTargetResolver targets,
        ConnectivityApiClient client,
        ConnectorResolver connectors,
        ConnectionReader reader,
        ConnectionConsentFlow consent,
        ILogger<ConnectionProvisioner>? logger = null)
    {
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _connectors = connectors ?? throw new ArgumentNullException(nameof(connectors));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _consent = consent ?? throw new ArgumentNullException(nameof(consent));
        _logger = logger ?? NullLogger<ConnectionProvisioner>.Instance;
    }

    public async Task<ConnectionCreateOutcome> CreateAsync(
        string? profileName, ConnectionCreateOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.DisplayName))
            throw new ArgumentException("A display name is required.", nameof(options));

        var target = await _targets.ResolveAsync(profileName, ct).ConfigureAwait(false);
        var connector = await _connectors.ResolveAsync(target, options.Connector, ct).ConfigureAwait(false);

        var existing = await FindExistingAsync(target, connector, options.DisplayName, ct).ConfigureAwait(false);
        if (existing is not null && ConnectionStatusCodes.IsConnected(existing.ConnectionStatus))
        {
            return new ConnectionCreateOutcome(
                ConnectionOutcomeStatus.Unchanged, existing.ConnectionId, connector.ApiId, options.DisplayName,
                existing.ConnectionStatus, null, null);
        }

        var pending = new PendingConnection(
            connector,
            existing?.ConnectionId ?? Guid.NewGuid().ToString(),
            options.DisplayName,
            existing is null ? ConnectionOutcomeStatus.Created : ConnectionOutcomeStatus.Updated);

        using var written = await WriteAsync(target, pending, options, ct).ConfigureAwait(false);
        var root = written.Body?.RootElement ?? default;
        var state = ConnectionJsonParser.ReadState(root);

        if (state.Error is null)
            return pending.Outcome(pending.HealthyStatus, state.Status, null, null);

        if (state.IsUnauthenticated)
            return await _consent.SettleAsync(target, pending, root, options.AttemptConsent, ct).ConfigureAwait(false);

        // The platform rejected the credentials (bad secret, unknown app, ...).
        // Do not leave a broken connection behind that this run created.
        if (existing is null)
            await TryDeleteAsync(target, pending, ct).ConfigureAwait(false);

        return pending.Outcome(ConnectionOutcomeStatus.Failed, state.Status, null, state.Error);
    }

    public async Task DeleteAsync(string? profileName, string? connector, string connectionId, CancellationToken ct)
    {
        var target = await _targets.ResolveAsync(profileName, ct).ConfigureAwait(false);
        var apiId = await _reader.ResolveApiIdAsync(target, connector, connectionId, ct).ConfigureAwait(false);

        await DeleteConnectionAsync(target, apiId, connectionId, ct).ConfigureAwait(false);
    }

    /// <summary>The caller's connection with this display name for the connector, if there is exactly one.</summary>
    /// <exception cref="ArgumentException">More than one matches, so the target of the create is ambiguous.</exception>
    private async Task<ConnectionSummary?> FindExistingAsync(
        ConnectivityTarget target, ResolvedConnectorInfo connector, string displayName, CancellationToken ct)
    {
        var matches = (await _reader.ListOwnedAsync(target, ct).ConfigureAwait(false))
            .Where(c => string.Equals(c.ApiId, connector.ApiId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(c.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count > 1)
        {
            throw new ArgumentException(
                $"{matches.Count} connections named '{displayName}' already exist for connector '{connector.LogicalName}': " +
                string.Join(", ", matches.Select(c => c.ConnectionId)) + ". Use a unique display name.");
        }

        return matches.SingleOrDefault();
    }

    /// <summary>Writes the connection (an upsert by id) with the credentials of the chosen authentication method.</summary>
    private async Task<ConnectivityResponse> WriteAsync(
        ConnectivityTarget target, PendingConnection pending, ConnectionCreateOptions options, CancellationToken ct)
    {
        var method = await SelectMethodAsync(target, pending.Connector, options, ct).ConfigureAwait(false);
        ConnectionRequestBuilder.Validate(method, options.Parameters, pending.Connector.LogicalName);

        if (method.IsInteractiveOAuth && !SilentConsentHelper.IsUserCredential(target.Credential))
        {
            throw new ArgumentException(
                $"Authentication method '{method.Name}' of connector '{pending.Connector.LogicalName}' signs in as a person, " +
                "which an application credential cannot do. Use a user credential, or an authentication method that takes " +
                "application credentials (for example ServicePrincipalOauth).");
        }

        var wantsConsentLink = method.IsInteractiveOAuth;
        var body = ConnectionRequestBuilder.BuildBody(
            pending.DisplayName, target.EnvironmentId, method, options.Parameters, wantsConsentLink);

        return await _client.SendAsync(
            HttpMethod.Put,
            ConnectivityEndpoints.Connection(target.BaseUri, pending.Connector.ApiId, pending.ConnectionId, wantsConsentLink),
            target, body, ct).ConfigureAwait(false);
    }

    private async Task<AuthMethod> SelectMethodAsync(
        ConnectivityTarget target, ResolvedConnectorInfo connector, ConnectionCreateOptions options, CancellationToken ct)
    {
        ConnectivityResponse response;
        try
        {
            response = await _client.SendAsync(
                HttpMethod.Get,
                ConnectivityEndpoints.Connector(target.BaseUri, connector.ApiId, target.EnvironmentId),
                target, null, ct).ConfigureAwait(false);
        }
        catch (ConnectivityRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new ConnectorNotFoundException(connector.LogicalName);
        }

        using (response)
        {
            var methods = response.Body is { } doc
                ? ConnectorAuthMethodReader.Read(doc.RootElement)
                : [new AuthMethod(ConnectorAuthMethodReader.NoAuthMethodName, null, false, [])];

            return ConnectionRequestBuilder.Select(
                methods, options.AuthMethod, options.Parameters.Keys.ToList(), connector.LogicalName);
        }
    }

    private async Task DeleteConnectionAsync(
        ConnectivityTarget target, string apiId, string connectionId, CancellationToken ct)
    {
        using var _ = await _client.SendForConnectionAsync(
            HttpMethod.Delete,
            ConnectivityEndpoints.ConnectionInEnvironment(target.BaseUri, apiId, connectionId, target.EnvironmentId),
            target, null, apiId, connectionId, ct).ConfigureAwait(false);
    }

    private async Task TryDeleteAsync(ConnectivityTarget target, PendingConnection pending, CancellationToken ct)
    {
        try
        {
            await DeleteConnectionAsync(target, pending.Connector.ApiId, pending.ConnectionId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not remove the connection {ConnectionId} after a failed create.", pending.ConnectionId);
        }
    }
}
