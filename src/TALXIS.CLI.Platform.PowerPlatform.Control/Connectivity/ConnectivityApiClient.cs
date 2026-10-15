using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;

/// <summary>A connectivity response: status plus the parsed body when there was one.</summary>
internal sealed record ConnectivityResponse(HttpStatusCode StatusCode, JsonDocument? Body) : IDisposable
{
    public void Dispose() => Body?.Dispose();
}

/// <summary>
/// Authenticated JSON transport for the <c>/connectivity/*</c> routes. It uses
/// the Power Platform API audience only — these routes reject the Power Apps
/// service audience with 401 — so there is no audience probe and nothing to
/// cache across route families.
/// </summary>
/// <remarks>
/// Several routes answer success with an empty body (delete, modifyPermissions,
/// consent), so an empty body is a valid, body-less response here.
/// </remarks>
internal sealed class ConnectivityApiClient
{
    private const int MaxRetries = 2;
    private const int InitialRetryDelayMs = 500;

    private readonly IAccessTokenService _tokens;
    private readonly IHttpClientFactoryWrapper _httpFactory;
    private readonly ILogger _logger;

    public ConnectivityApiClient(
        IAccessTokenService tokens,
        IHttpClientFactoryWrapper? httpFactory = null,
        ILogger? logger = null)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _httpFactory = httpFactory ?? DefaultHttpClientFactoryWrapper.Instance;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Sends a request and returns the response. A 404 surfaces as
    /// <see cref="ArgumentException"/> (bad input, exit 2); any other failure as
    /// <see cref="InvalidOperationException"/> carrying the platform's own
    /// error code and message.
    /// </summary>
    public async Task<ConnectivityResponse> SendAsync(
        HttpMethod method,
        Uri requestUri,
        Connection connection,
        Credential credential,
        JsonNode? jsonBody,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(credential);

        var token = await _tokens.AcquireForResourceAsync(connection, credential, ConnectivityEndpoints.Audience, ct)
            .ConfigureAwait(false);

        RawResponse raw = default;
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            raw = await SendOnceAsync(method, requestUri, token, jsonBody, ct).ConfigureAwait(false);

            var transient = raw.StatusCode == HttpStatusCode.TooManyRequests || (int)raw.StatusCode >= 500;
            if (!transient || attempt == MaxRetries)
                break;

            var delay = InitialRetryDelayMs * (int)Math.Pow(2, attempt);
            _logger.LogDebug("Connectivity request to {Uri} returned {Status}; retrying in {Delay}ms.",
                requestUri, (int)raw.StatusCode, delay);
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }

        if ((int)raw.StatusCode is < 200 or >= 300)
            throw Failure(raw, method, requestUri);

        return new ConnectivityResponse(raw.StatusCode, TryParse(raw.Body));
    }

    /// <summary>
    /// The caller's Entra object id, read from the <c>oid</c> claim of a token
    /// for the connectivity audience. Needed to list connections the caller
    /// owns: the platform only returns another identity's connection when the
    /// permissions expand names the caller.
    /// </summary>
    public async Task<string> GetCallerObjectIdAsync(Connection connection, Credential credential, CancellationToken ct)
    {
        var token = await _tokens.AcquireForResourceAsync(connection, credential, ConnectivityEndpoints.Audience, ct)
            .ConfigureAwait(false);
        return JwtClaims.GetObjectId(token)
            ?? throw new InvalidOperationException(
                "The access token for the Power Platform API carries no 'oid' claim, so the caller's connections cannot be listed.");
    }

    private async Task<RawResponse> SendOnceAsync(
        HttpMethod method, Uri requestUri, string token, JsonNode? jsonBody, CancellationToken ct)
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

    private static JsonDocument? TryParse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Exception Failure(RawResponse raw, HttpMethod method, Uri requestUri)
    {
        var (code, message) = ReadError(raw.Body);
        var detail = string.IsNullOrEmpty(code) && string.IsNullOrEmpty(message)
            ? Truncate(raw.Body, 500)
            : $"{code}: {message}".Trim(' ', ':');
        var text = $"{method} {requestUri.AbsolutePath} failed ({(int)raw.StatusCode} {raw.StatusCode}): {detail}";

        return raw.StatusCode == HttpStatusCode.NotFound
            ? new ArgumentException(text)
            : new InvalidOperationException(text);
    }

    /// <summary>Reads <c>error.code</c> and <c>error.message</c> from a platform error body.</summary>
    internal static (string? Code, string? Message) ReadError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return (null, null);
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                var error = root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object ? e : root;
                string? Read(string name) =>
                    error.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                return (Read("code"), Read("message"));
            }
        }
        catch (JsonException)
        {
            // Not JSON: the caller falls back to the raw text.
        }
        return (null, null);
    }

    internal static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s[..max] + "...");

    private readonly record struct RawResponse(HttpStatusCode StatusCode, string Body);
}
