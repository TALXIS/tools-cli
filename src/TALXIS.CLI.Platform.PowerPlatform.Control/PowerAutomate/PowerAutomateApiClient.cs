using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.PowerAutomate;

/// <summary>
/// Authenticated JSON transport for the Power Automate metadata endpoints on
/// the per-environment Power Platform API. Owns token acquisition (including
/// the audience probe), transient retries, and HTTP error interpretation, so
/// the service layer deals only in payload shapes.
/// </summary>
internal sealed class PowerAutomateApiClient
{
    private const int MaxRetries = 2;
    private const int InitialRetryDelayMs = 500;

    /// <summary>
    /// Which audience the <c>/powerautomate/*</c> endpoints accept, learned at
    /// runtime and remembered per cloud. The Power Platform API resource is
    /// tried first because txc already uses it against this host; Microsoft's
    /// own tooling uses the Flow service resource, so that is the fallback.
    /// Populated on first success to keep later calls to one token request.
    /// </summary>
    private static readonly ConcurrentDictionary<CloudInstance, Uri> ResolvedAudiences = new();

    private readonly IAccessTokenService _tokens;
    private readonly IHttpClientFactoryWrapper _httpFactory;
    private readonly ILogger _logger;

    public PowerAutomateApiClient(
        IAccessTokenService tokens,
        IHttpClientFactoryWrapper? httpFactory = null,
        ILogger<PowerAutomateApiClient>? logger = null)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _httpFactory = httpFactory ?? DefaultHttpClientFactoryWrapper.Instance;
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
        var audiences = GetAudienceCandidates(cloud);

        Exception? lastFailure = null;

        for (var i = 0; i < audiences.Count; i++)
        {
            var audience = audiences[i];
            var isLastAudience = i == audiences.Count - 1;

            var token = await _tokens.AcquireForResourceAsync(connection, credential, audience, ct)
                .ConfigureAwait(false);

            var response = await SendWithRetriesAsync(method, requestUri, token, jsonBody, ct).ConfigureAwait(false);

            if (response.IsSuccess)
            {
                // Remember the audience that worked so subsequent calls in this
                // process skip the probe entirely.
                ResolvedAudiences[cloud] = audience;
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
    /// Audience order for a cloud: the one already proven to work, else the
    /// Power Platform API resource followed by the Flow service resource.
    /// </summary>
    private static IReadOnlyList<Uri> GetAudienceCandidates(CloudInstance cloud)
    {
        if (ResolvedAudiences.TryGetValue(cloud, out var known))
            return new[] { known };

        var flow = PowerAutomateEndpointProvider.GetFlowServiceAudience(cloud);
        return new[] { PowerAutomateEndpointProvider.PowerPlatformApiAudience, flow };
    }

    private async Task<RawResponse> SendWithRetriesAsync(
        HttpMethod method,
        Uri requestUri,
        string token,
        JsonNode? jsonBody,
        CancellationToken ct)
    {
        RawResponse response = default;

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            response = await SendOnceAsync(method, requestUri, token, jsonBody, ct).ConfigureAwait(false);

            var transient = response.StatusCode == HttpStatusCode.TooManyRequests
                || (int)response.StatusCode >= 500;

            if (!transient || attempt == MaxRetries)
                return response;

            var delay = InitialRetryDelayMs * (int)Math.Pow(2, attempt);
            _logger.LogDebug(
                "Power Automate metadata request to {Uri} returned {Status}; retrying in {Delay}ms.",
                requestUri, (int)response.StatusCode, delay);
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }

        return response;
    }

    private async Task<RawResponse> SendOnceAsync(
        HttpMethod method,
        Uri requestUri,
        string token,
        JsonNode? jsonBody,
        CancellationToken ct)
    {
        using var http = _httpFactory.Create();
        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (jsonBody is not null)
            request.Content = new StringContent(jsonBody.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return new RawResponse(response.StatusCode, body);
    }

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
                $"Power Automate metadata request to '{requestUri}' returned a non-JSON response: {Truncate(body, 300)}",
                ex);
        }
    }

    private static Exception Failure(RawResponse response, Uri requestUri)
    {
        var detail = $"({(int)response.StatusCode} {response.StatusCode}): {Truncate(response.Body, 500)}";

        // A 404 here means the connector or operation does not exist, which is
        // bad input rather than a runtime fault — commands map it to exit 2.
        return response.StatusCode == HttpStatusCode.NotFound
            ? new ArgumentException($"Power Automate metadata not found at '{requestUri}' {detail}")
            : new InvalidOperationException($"Power Automate metadata request to '{requestUri}' failed {detail}");
    }

    internal static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s[..max] + "...");

    /// <summary>Test seam: forget the learned audience so a probe runs again.</summary>
    internal static void ResetResolvedAudiences() => ResolvedAudiences.Clear();

    private readonly record struct RawResponse(HttpStatusCode StatusCode, string Body)
    {
        public bool IsSuccess => (int)StatusCode is >= 200 and < 300;
    }
}

