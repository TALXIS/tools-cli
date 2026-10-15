using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control.Http;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.PowerAutomate;

/// <summary>
/// Authenticated JSON transport for the Power Automate metadata endpoints on
/// the per-environment Power Platform API. Owns token acquisition (including
/// the audience probe), transient retries, and HTTP error interpretation, so
/// the service layer deals only in payload shapes.
/// </summary>
internal sealed class PowerAutomateApiClient
{
    /// <summary>
    /// Which audience the <c>/powerautomate/*</c> endpoints accept, learned at
    /// runtime and remembered per cloud so later calls make one token request
    /// rather than repeating the probe. See
    /// <see cref="GetAudienceCandidates"/> for the order and why.
    /// </summary>
    private static readonly ConcurrentDictionary<(CloudInstance Cloud, bool Connectivity), Uri> ResolvedAudiences = new();

    private readonly IAccessTokenService _tokens;
    private readonly RetryingHttpSender _sender;
    private readonly ILogger _logger;

    public PowerAutomateApiClient(
        IAccessTokenService tokens,
        RetryingHttpSender sender,
        ILogger<PowerAutomateApiClient>? logger = null)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _logger = logger ?? NullLogger<PowerAutomateApiClient>.Instance;
    }

    /// <summary>
    /// Sends a request and returns the parsed JSON body. The caller owns the
    /// returned <see cref="JsonDocument"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The resource does not exist (404) — surfaces as a validation error so
    /// an unknown connector or operation is not reported as a runtime fault.
    /// </exception>
    /// <exception cref="InvalidOperationException">Any other non-success response.</exception>
    public async Task<JsonDocument> SendAsync(
        HttpMethod method,
        Uri requestUri,
        Connection connection,
        Credential credential,
        JsonNode? jsonBody,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(credential);

        var cloud = connection.Cloud ?? CloudInstance.Public;
        var connectivity = IsConnectivityRoute(requestUri);
        var audiences = GetAudienceCandidates(cloud, connectivity);

        Exception? lastFailure = null;

        for (var i = 0; i < audiences.Count; i++)
        {
            var audience = audiences[i];
            var isLastAudience = i == audiences.Count - 1;

            string token;
            try
            {
                token = await _tokens.AcquireForResourceAsync(connection, credential, audience, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (!isLastAudience)
            {
                // Some resources cannot be issued a token for this application
                // at all (Entra AADSTS65002). That rules the audience out; it
                // must not rule out the ones still untried.
                _logger.LogDebug(ex, "Could not obtain a token for audience {Audience}; trying the next one.", audience);
                lastFailure = ex;
                continue;
            }

            var response = await _sender.SendAsync(method, requestUri, token, jsonBody, ct).ConfigureAwait(false);

            if (response.IsSuccess)
            {
                // Remember the audience that worked so subsequent calls in this
                // process skip the probe entirely.
                ResolvedAudiences[(cloud, connectivity)] = audience;
                return Parse(response.Body, requestUri);
            }

            // Only an authentication/authorization rejection says anything about
            // the audience. Everything else is a real error for this request.
            var audienceRejected = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
            if (!audienceRejected || isLastAudience)
                throw Failure(response, requestUri);

            _logger.LogDebug(
                "Power Automate metadata request to {Uri} was rejected ({Status}) for audience {Audience}; retrying with the next audience.",
                requestUri, (int)response.StatusCode, audience);

            lastFailure = Failure(response, requestUri);
        }

        throw lastFailure ?? new InvalidOperationException(
            $"Power Automate metadata request to '{requestUri}' could not be authenticated.");
    }

    /// <summary>
    /// Audience order for a route family on a cloud: the one already proven to
    /// work, else the family's expected audience followed by the other.
    /// </summary>
    /// <remarks>
    /// The two families accept different audiences on the same host:
    /// <c>/powerautomate/*</c> takes the Power Apps service token and rejects a
    /// Power Platform API token, while <c>/connectivity/*</c> takes the Power
    /// Platform API token and rejects the Power Apps one with 401. A single
    /// per-cloud cache would let one family's probe poison the other.
    /// </remarks>
    private static IReadOnlyList<Uri> GetAudienceCandidates(CloudInstance cloud, bool connectivity)
    {
        if (ResolvedAudiences.TryGetValue((cloud, connectivity), out var known))
            return new[] { known };

        // Microsoft's own tooling uses the Flow service resource for the
        // powerautomate routes, but Entra refuses to issue the pinned pac
        // application a token for it (AADSTS65002), so probing it would only
        // ever cost a round trip.
        return connectivity
            ? new[]
            {
                PowerAutomateEndpointProvider.PowerPlatformApiAudience,
                PowerAutomateEndpointProvider.PowerAppsServiceAudience,
            }
            : new[]
            {
                PowerAutomateEndpointProvider.PowerAppsServiceAudience,
                PowerAutomateEndpointProvider.PowerPlatformApiAudience,
            };
    }

    private static bool IsConnectivityRoute(Uri requestUri)
        => requestUri.AbsolutePath.StartsWith("/connectivity/", StringComparison.OrdinalIgnoreCase);

    private static JsonDocument Parse(string body, Uri requestUri)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new InvalidOperationException(
                $"Power Automate metadata request to '{requestUri}' returned an empty response body.");
        }

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Power Automate metadata request to '{requestUri}' returned a non-JSON response: {HttpErrorText.Truncate(body, 300)}",
                ex);
        }
    }

    private static Exception Failure(HttpResult response, Uri requestUri)
    {
        var detail = $"({(int)response.StatusCode} {response.StatusCode}): {HttpErrorText.Truncate(response.Body, 500)}";

        // A 404 here means the connector or operation does not exist, which is
        // bad input rather than a runtime fault — commands map it to exit 2.
        return response.StatusCode == HttpStatusCode.NotFound
            ? new ArgumentException($"Power Automate metadata not found at '{requestUri}' {detail}")
            : new InvalidOperationException($"Power Automate metadata request to '{requestUri}' failed {detail}");
    }

    /// <summary>Test seam: forget the learned audience so a probe runs again.</summary>
    internal static void ResetResolvedAudiences() => ResolvedAudiences.Clear();
}
