using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Identity;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control.Http;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;

/// <summary>Result of an attempt to complete OAuth consent without a browser.</summary>
internal sealed record ConsentAttempt(bool Completed, string? Failure);

/// <summary>
/// Completes OAuth consent for a user-delegated connection without a browser,
/// and calls a connection's test link, both with an API Hub token.
/// </summary>
/// <remarks>
/// The first-party consent flow is not documented by Microsoft and may change.
/// When the new connection's <c>consentInfo</c> carries a <c>firstPartyLoginUri</c>
/// (the connector's OAuth setting supports on-behalf-of login), POSTing the
/// signed-in user's API Hub access token as <c>{"accessToken": ...}</c> to that
/// URI, with no Authorization header, lets the consent service exchange it on
/// the user's behalf and authenticate the connection. It only accepts user
/// assertions, so it is attempted for interactive and device-code credentials
/// only, never for a service principal.
/// </remarks>
internal sealed class SilentConsentHelper
{
    private readonly IAccessTokenService _tokens;
    private readonly IHttpClientFactoryWrapper _httpFactory;
    private readonly ILogger _logger;

    public SilentConsentHelper(
        IAccessTokenService tokens,
        IHttpClientFactoryWrapper? httpFactory = null,
        ILogger<SilentConsentHelper>? logger = null)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _httpFactory = httpFactory ?? DefaultHttpClientFactoryWrapper.Instance;
        _logger = logger ?? NullLogger<SilentConsentHelper>.Instance;
    }

    /// <summary>True for credentials that represent a person, which consent requires.</summary>
    public static bool IsUserCredential(Credential credential)
        => credential.Kind is CredentialKind.InteractiveBrowser or CredentialKind.DeviceCode;

    public async Task<ConsentAttempt> TryCompleteAsync(
        Connection connection, Credential credential, Uri firstPartyLoginUri, CancellationToken ct)
    {
        if (!IsUserCredential(credential))
            return new ConsentAttempt(false, "Consent needs a user credential; this profile uses an application credential.");

        string token;
        try
        {
            token = await AcquireApiHubTokenAsync(connection, credential, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Could not acquire an API Hub token for silent consent.");
            return new ConsentAttempt(false, "Could not acquire an API Hub token: " + ex.Message);
        }

        var body = JsonSerializer.Serialize(new Dictionary<string, string> { ["accessToken"] = token });

        using var http = _httpFactory.Create();
        using var request = new HttpRequestMessage(HttpMethod.Post, firstPartyLoginUri)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
            return new ConsentAttempt(true, null);

        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return new ConsentAttempt(false,
            $"The consent service rejected the sign-in ({(int)response.StatusCode}): {HttpErrorText.Truncate(text, 300)}");
    }

    /// <summary>
    /// Calls a connection's test link. Healthy when the connector answers 200 or 204.
    /// </summary>
    public async Task<(bool Healthy, string? Message)> RunTestLinkAsync(
        Connection connection, Credential credential, Uri testLink, CancellationToken ct)
    {
        string token;
        try
        {
            token = await AcquireApiHubTokenAsync(connection, credential, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Could not acquire an API Hub token for the connection test.");
            return (false, "Could not acquire an API Hub token for the test call: " + ex.Message);
        }

        using var http = _httpFactory.Create();
        using var request = new HttpRequestMessage(HttpMethod.Get, testLink);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        var code = (int)response.StatusCode;
        if (code is 200 or 204)
            return (true, null);

        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return (false, $"The connection test returned {code}: {HttpErrorText.Truncate(text, 300)}");
    }

    private Task<string> AcquireApiHubTokenAsync(Connection connection, Credential credential, CancellationToken ct)
        => _tokens.AcquireForResourceAsync(
            connection, credential, ApiHubAudiences.For(connection.Cloud ?? CloudInstance.Public), ct);
}
