using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.CLI.Core.Abstractions;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Http;

/// <summary>A raw HTTP answer: the status and the body read as text.</summary>
internal readonly record struct HttpResult(HttpStatusCode StatusCode, string Body)
{
    public bool IsSuccess => (int)StatusCode is >= 200 and < 300;
}

/// <summary>
/// Sends one bearer-authenticated JSON request, retrying throttling (429) and
/// server errors (5xx) with exponential backoff. Interpreting the answer is the
/// caller's job.
/// </summary>
internal sealed class RetryingHttpSender
{
    private const int MaxRetries = 2;
    private const int InitialRetryDelayMs = 500;

    private readonly IHttpClientFactoryWrapper _httpFactory;
    private readonly ILogger _logger;

    public RetryingHttpSender(
        IHttpClientFactoryWrapper? httpFactory = null,
        ILogger<RetryingHttpSender>? logger = null)
    {
        _httpFactory = httpFactory ?? DefaultHttpClientFactoryWrapper.Instance;
        _logger = logger ?? NullLogger<RetryingHttpSender>.Instance;
    }

    public async Task<HttpResult> SendAsync(
        HttpMethod method, Uri requestUri, string token, JsonNode? jsonBody, CancellationToken ct)
    {
        HttpResult result = default;

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            result = await SendOnceAsync(method, requestUri, token, jsonBody, ct).ConfigureAwait(false);

            var transient = result.StatusCode == HttpStatusCode.TooManyRequests || (int)result.StatusCode >= 500;
            if (!transient || attempt == MaxRetries)
                break;

            var delay = InitialRetryDelayMs * (int)Math.Pow(2, attempt);
            _logger.LogDebug("Request to {Uri} returned {Status}; retrying in {Delay}ms.",
                requestUri, (int)result.StatusCode, delay);
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }

        return result;
    }

    private async Task<HttpResult> SendOnceAsync(
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
        return new HttpResult(response.StatusCode, body);
    }
}

internal static class HttpErrorText
{
    public static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s[..max] + "...");
}
