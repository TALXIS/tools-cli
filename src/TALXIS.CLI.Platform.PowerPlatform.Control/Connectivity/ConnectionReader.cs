using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;

/// <summary>Lists, reads and tests the connections the caller owns or can use.</summary>
internal sealed class ConnectionReader : IConnectionReader
{
    private const int MaxPages = 20;

    private readonly ConnectivityTargetResolver _targets;
    private readonly ConnectivityApiClient _client;
    private readonly ConnectorResolver _connectors;
    private readonly SilentConsentHelper _consent;
    private readonly ILogger _logger;

    public ConnectionReader(
        ConnectivityTargetResolver targets,
        ConnectivityApiClient client,
        ConnectorResolver connectors,
        SilentConsentHelper consent,
        ILogger<ConnectionReader>? logger = null)
    {
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _connectors = connectors ?? throw new ArgumentNullException(nameof(connectors));
        _consent = consent ?? throw new ArgumentNullException(nameof(consent));
        _logger = logger ?? NullLogger<ConnectionReader>.Instance;
    }

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
        string? profileName, string? connector, string connectionId, CancellationToken ct)
    {
        var target = await _targets.ResolveAsync(profileName, ct).ConfigureAwait(false);
        var apiId = await ResolveApiIdAsync(target, connector, connectionId, ct).ConfigureAwait(false);
        using var document = await GetDocumentAsync(target, apiId, connectionId, ct).ConfigureAwait(false);
        return ConnectionJsonParser.ParseConnection(document.RootElement, apiId);
    }

    /// <summary>
    /// The apiId a connection id lives under: the named connector's, or, when none is
    /// named, the connector of the one connection the caller can see with this id.
    /// </summary>
    /// <exception cref="ConnectionNotFoundException">No connection with this id is visible to the caller.</exception>
    /// <exception cref="AmbiguousConnectionException">Several connectors have a connection with this id.</exception>
    public async Task<string> ResolveApiIdAsync(
        ConnectivityTarget target, string? connector, string connectionId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(connector))
            return (await _connectors.ResolveAsync(target, connector, ct).ConfigureAwait(false)).ApiId;

        var apiIds = (await ListOwnedAsync(target, ct).ConfigureAwait(false))
            .Where(c => string.Equals(c.ConnectionId, connectionId, StringComparison.Ordinal))
            .Select(c => c.ApiId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return apiIds.Count switch
        {
            0 => throw new ConnectionNotFoundException(connectionId),
            1 => apiIds[0],
            _ => throw new AmbiguousConnectionException(connectionId, apiIds),
        };
    }

    /// <summary>
    /// Healthy when the platform says the connection is connected and, for a
    /// connector that exposes a test link, the live call succeeds.
    /// </summary>
    public async Task<ConnectionTestOutcome> TestAsync(
        string? profileName, string? connector, string connectionId, CancellationToken ct)
    {
        var target = await _targets.ResolveAsync(profileName, ct).ConfigureAwait(false);
        var apiId = await ResolveApiIdAsync(target, connector, connectionId, ct).ConfigureAwait(false);
        using var document = await GetDocumentAsync(target, apiId, connectionId, ct).ConfigureAwait(false);
        var summary = ConnectionJsonParser.ParseConnection(document.RootElement, apiId);

        if (!ConnectionStatusCodes.IsConnected(summary.ConnectionStatus))
            return new ConnectionTestOutcome(false, summary.ConnectionStatus, summary.StatusMessage);

        var testLink = ConnectionJsonParser.ReadTestLink(document.RootElement);
        if (testLink is null)
            return new ConnectionTestOutcome(true, summary.ConnectionStatus, null);

        var (healthy, message) = await _consent.RunTestLinkAsync(target.Connection, target.Credential, testLink, ct)
            .ConfigureAwait(false);
        return new ConnectionTestOutcome(healthy, summary.ConnectionStatus, message);
    }

    /// <summary>Every connection the caller owns or can use, across all pages.</summary>
    public async Task<IReadOnlyList<ConnectionSummary>> ListOwnedAsync(ConnectivityTarget target, CancellationToken ct)
    {
        var callerId = await _client.GetCallerObjectIdAsync(target, ct).ConfigureAwait(false);
        var uri = ConnectivityEndpoints.Connections(target.BaseUri, target.EnvironmentId, callerId);
        var result = new List<ConnectionSummary>();

        for (var page = 0; page < MaxPages && uri is not null; page++)
        {
            using var response = await _client.SendAsync(HttpMethod.Get, uri, target, null, ct).ConfigureAwait(false);
            if (response.Body is not { } doc)
                return result;

            result.AddRange(ParsePage(doc.RootElement));
            uri = NextLinkOf(doc.RootElement);
        }

        if (uri is not null)
        {
            _logger.LogWarning(
                "The connection list has more than {Pages} pages; connections beyond them are not included.", MaxPages);
        }

        return result;
    }

    /// <summary>The current state of one connection.</summary>
    public async Task<ConnectionState> GetStateAsync(
        ConnectivityTarget target, string apiId, string connectionId, CancellationToken ct)
    {
        using var document = await GetDocumentAsync(target, apiId, connectionId, ct).ConfigureAwait(false);
        return ConnectionJsonParser.ReadState(document.RootElement);
    }

    private async Task<JsonDocument> GetDocumentAsync(
        ConnectivityTarget target, string apiId, string connectionId, CancellationToken ct)
    {
        var uri = ConnectivityEndpoints.ConnectionInEnvironment(target.BaseUri, apiId, connectionId, target.EnvironmentId);
        var response = await _client.SendForConnectionAsync(HttpMethod.Get, uri, target, null, apiId, connectionId, ct)
            .ConfigureAwait(false);

        return response.Body
            ?? throw new InvalidOperationException($"The platform returned no body for connection '{connectionId}'.");
    }

    private static IEnumerable<ConnectionSummary> ParsePage(JsonElement root)
    {
        if (!root.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var item in value.EnumerateArray())
        {
            var apiId = ConnectionJsonParser.ApiIdOf(item);
            if (!string.IsNullOrEmpty(apiId))
                yield return ConnectionJsonParser.ParseConnection(item, apiId);
        }
    }

    private static Uri? NextLinkOf(JsonElement root)
        => JsonElementReader.TryGetString(root, "nextLink") is { } next
            && Uri.TryCreate(next, UriKind.Absolute, out var uri)
                ? uri
                : null;
}
