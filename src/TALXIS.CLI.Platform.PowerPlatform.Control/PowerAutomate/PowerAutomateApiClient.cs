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
/// Thin authenticated JSON transport for the Power Automate metadata routes.
/// Acquires one token for <see cref="PowerAutomateEndpointProvider.Audience"/>
/// per request batch, retries transient failures, and maps HTTP errors, so the
/// service layer deals only in payload shapes.
/// </summary>
internal sealed class PowerAutomateApiClient
{
    private const int MaxRetries = 2;
    private const int InitialRetryDelayMs = 500;

    private readonly IAccessTokenService _tokens;
    private readonly IHttpClientFactoryWrapper _httpFactory;
    private readonly ILogger _logger;

    public PowerAutomateApiClient(
        IAccessTokenService tokens,
        IHttpClientFactoryWrapper? httpFactory = null,
        ILogger? logger = null)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _httpFactory = httpFactory ?? DefaultHttpClientFactoryWrapper.Instance;
        _logger = logger ?? NullLogger.Instance;
    }

    public Task<string> AcquireTokenAsync(Connection connection, Credential credential, CancellationToken ct)
        => _tokens.AcquireForResourceAsync(connection, credential, PowerAutomateEndpointProvider.Audience, ct);

    /// <summary>
    /// Sends a bearer-authorized request and returns the parsed JSON body. The
    /// caller owns the returned <see cref="JsonDocument"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The connector or operation does not exist (404) — bad input rather than
    /// a runtime fault, so commands report it as a validation error.
    /// </exception>
    /// <exception cref="InvalidOperationException">Any other non-success response.</exception>
    public async Task<JsonDocument> SendAsync(
        HttpMethod method,
        Uri requestUri,
        string token,
        JsonNode? jsonBody,
        CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var (status, body) = await SendOnceAsync(method, requestUri, token, jsonBody, ct).ConfigureAwait(false);

            if ((int)status is >= 200 and < 300)
                return Parse(body, requestUri);

            var transient = status == HttpStatusCode.TooManyRequests || (int)status >= 500;
            if (!transient || attempt == MaxRetries)
                throw Failure(status, body, requestUri);

            var delay = InitialRetryDelayMs * (1 << attempt);
            _logger.LogDebug(
                "Power Automate metadata request to {Uri} returned {Status}; retrying in {Delay}ms.",
                requestUri, (int)status, delay);
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
    }

    private async Task<(HttpStatusCode Status, string Body)> SendOnceAsync(
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
        return (response.StatusCode, body);
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

    private static Exception Failure(HttpStatusCode status, string body, Uri requestUri)
    {
        var detail = $"({(int)status} {status}): {Truncate(body, 500)}";
        return status == HttpStatusCode.NotFound
            ? new ArgumentException($"Power Automate metadata not found at '{requestUri}' {detail}")
            : new InvalidOperationException($"Power Automate metadata request to '{requestUri}' failed {detail}");
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s[..max] + "...");
}
