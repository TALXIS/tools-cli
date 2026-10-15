using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Platform.PowerPlatform.Control.Http;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;

/// <summary>A connectivity response: status plus the parsed body when there was one.</summary>
internal sealed record ConnectivityResponse(HttpStatusCode StatusCode, JsonDocument? Body) : IDisposable
{
    public void Dispose() => Body?.Dispose();
}

/// <summary>A connectivity request the platform answered with a failure status.</summary>
internal sealed class ConnectivityRequestException : InvalidOperationException
{
    public ConnectivityRequestException(string message, HttpStatusCode statusCode)
        : base(message)
        => StatusCode = statusCode;

    public HttpStatusCode StatusCode { get; }
}

/// <summary>
/// Authenticated JSON transport for the <c>/connectivity/*</c> routes. It uses
/// the Power Platform API audience only: these routes reject the Power Apps
/// service audience with 401.
/// </summary>
/// <remarks>
/// Several routes answer success with an empty body (delete, modifyPermissions,
/// consent), so an empty body is a valid, body-less response here.
/// </remarks>
internal sealed class ConnectivityApiClient
{
    private readonly IAccessTokenService _tokens;
    private readonly RetryingHttpSender _sender;

    public ConnectivityApiClient(IAccessTokenService tokens, RetryingHttpSender sender)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
    }

    /// <summary>
    /// Sends a request and returns the response. Any failure status surfaces as
    /// <see cref="ConnectivityRequestException"/> carrying the platform's own
    /// error code and message; the status alone says nothing about which
    /// resource was missing, so callers that know translate it.
    /// </summary>
    public async Task<ConnectivityResponse> SendAsync(
        HttpMethod method, Uri requestUri, ConnectivityTarget target, JsonNode? jsonBody, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);

        var token = await AcquireTokenAsync(target, ct).ConfigureAwait(false);
        var result = await _sender.SendAsync(method, requestUri, token, jsonBody, ct).ConfigureAwait(false);

        if (!result.IsSuccess)
            throw Failure(result, method, requestUri);

        return new ConnectivityResponse(result.StatusCode, TryParse(result.Body));
    }

    /// <summary>
    /// <see cref="SendAsync"/> for a request about one connection: a 404 means
    /// that connection does not exist, and a refused read (403) means the
    /// caller may not use it. Other failures stay as they are.
    /// </summary>
    public async Task<ConnectivityResponse> SendForConnectionAsync(
        HttpMethod method,
        Uri requestUri,
        ConnectivityTarget target,
        JsonNode? jsonBody,
        string apiId,
        string connectionId,
        CancellationToken ct)
    {
        try
        {
            return await SendAsync(method, requestUri, target, jsonBody, ct).ConfigureAwait(false);
        }
        catch (ConnectivityRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ConnectionNotFoundException(connectionId, apiId);
        }
        catch (ConnectivityRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden && method == HttpMethod.Get)
        {
            throw new ConnectionAccessDeniedException(connectionId, apiId);
        }
    }

    /// <summary>
    /// The caller's Entra object id, read from the <c>oid</c> claim of a token
    /// for the connectivity audience. Needed to list connections the caller
    /// owns: the platform only returns another identity's connection when the
    /// permissions expand names the caller.
    /// </summary>
    public async Task<string> GetCallerObjectIdAsync(ConnectivityTarget target, CancellationToken ct)
    {
        var token = await AcquireTokenAsync(target, ct).ConfigureAwait(false);
        return JwtClaims.GetObjectId(token)
            ?? throw new InvalidOperationException(
                "The access token for the Power Platform API carries no 'oid' claim, so the caller's connections cannot be listed.");
    }

    private Task<string> AcquireTokenAsync(ConnectivityTarget target, CancellationToken ct)
        => _tokens.AcquireForResourceAsync(target.Connection, target.Credential, ConnectivityEndpoints.Audience, ct);

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

    private static ConnectivityRequestException Failure(HttpResult result, HttpMethod method, Uri requestUri)
    {
        var (code, message) = ReadError(result.Body);
        var detail = string.IsNullOrEmpty(code) && string.IsNullOrEmpty(message)
            ? HttpErrorText.Truncate(result.Body, 500)
            : $"{code}: {message}".Trim(' ', ':');

        return new ConnectivityRequestException(
            $"{method} {requestUri.AbsolutePath} failed ({(int)result.StatusCode} {result.StatusCode}): {detail}",
            result.StatusCode);
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
                var error = JsonElementReader.TryGetObject(root, "error") ?? root;
                return (JsonElementReader.TryGetString(error, "code"), JsonElementReader.TryGetString(error, "message"));
            }
        }
        catch (JsonException)
        {
            // Not JSON: the caller falls back to the raw text.
        }
        return (null, null);
    }
}
