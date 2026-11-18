using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Identity;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control.Http;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.PowerAutomate;

/// <summary>
/// Thin authenticated JSON transport for the Power Automate metadata routes.
/// Acquires one token for <see cref="PowerAutomateEndpointProvider.Audience"/>
/// per request batch and maps HTTP errors, so the service layer deals only in
/// payload shapes. Transient failures are retried by <see cref="RetryingHttpSender"/>.
/// </summary>
internal sealed class PowerAutomateApiClient
{
    private readonly IAccessTokenService _tokens;
    private readonly RetryingHttpSender _sender;

    public PowerAutomateApiClient(IAccessTokenService tokens, RetryingHttpSender sender)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
    }

    public Task<string> AcquireTokenAsync(Connection connection, Credential credential, CancellationToken ct)
        => _tokens.AcquireForResourceAsync(connection, credential, PowerAutomateEndpointProvider.Audience, ct);

    /// <summary>Token for calling a connector's runtime through a connection.</summary>
    /// <remarks>
    /// Verified live: the runtime's token exchange allows the audience
    /// <c>https://apihub.azure.com</c> but rejects <c>https://apihub.azure.com/</c>.
    /// The token service requests API Hub audiences with the single-slash scope
    /// (<see cref="ApiHubAudiences"/>).
    /// </remarks>
    public Task<string> AcquireRuntimeTokenAsync(Connection connection, Credential credential, CancellationToken ct)
        => _tokens.AcquireForResourceAsync(connection, credential, ApiHubAudiences.For(connection.Cloud ?? CloudInstance.Public), ct);

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
        CancellationToken ct,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        var result = await _sender.SendAsync(method, requestUri, token, jsonBody, ct, headers).ConfigureAwait(false);
        return result.IsSuccess
            ? Parse(result.Body, requestUri)
            : throw Failure(result.StatusCode, result.Body, requestUri);
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
                $"Power Automate metadata request to '{requestUri}' returned a non-JSON response: {HttpErrorText.Truncate(body, 300)}",
                ex);
        }
    }

    private static Exception Failure(HttpStatusCode status, string body, Uri requestUri)
    {
        var detail = $"({(int)status} {status}): {HttpErrorText.Truncate(body, 500)}";
        return status == HttpStatusCode.NotFound
            ? new ArgumentException($"Power Automate metadata not found at '{requestUri}' {detail}")
            : new InvalidOperationException($"Power Automate metadata request to '{requestUri}' failed {detail}");
    }
}
