using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TALXIS.CLI.Platform.Dataverse.Runtime.Authority;

/// <summary>
/// Resolves the Entra authority URI for a Dataverse environment by issuing
/// an unauthenticated request and parsing the <c>WWW-Authenticate</c>
/// header's <c>authorization_uri</c> parameter.
/// </summary>
/// <remarks>
/// Mirrors <c>IAuthorityResolver.GetAuthority(environmentUrl)</c> in pac
/// (see <c>temp/pac-auth-research.md</c>). Used when the tenant is unknown
/// or when validating sovereign-cloud deployments that don't map cleanly
/// to a host suffix. Prefer <see cref="DataverseCloudMap"/> when the cloud
/// is already known on the Connection.
/// </remarks>
public sealed class AuthorityChallengeResolver
{
    private static readonly Uri WhoAmIPath = new("/api/data/v9.2/WhoAmI", UriKind.Relative);

    private readonly HttpClient _http;
    private readonly ILogger _logger;
    private readonly bool _disposeHttp;

    public AuthorityChallengeResolver(HttpClient? http = null, ILogger<AuthorityChallengeResolver>? logger = null)
    {
#pragma warning disable RS0030 // Single-use HTTP client for authority discovery when no shared client is provided
        _http = http ?? new HttpClient();
#pragma warning restore RS0030
        _disposeHttp = http is null;
        _logger = logger ?? NullLogger<AuthorityChallengeResolver>.Instance;
    }

    /// <summary>
    /// Issues an unauthenticated GET to <c>/api/data/v9.2/WhoAmI</c> on the
    /// environment URL and returns the <c>authorization_uri</c> reported by
    /// Dataverse's <c>WWW-Authenticate: Bearer</c> challenge.
    /// Throws <see cref="InvalidOperationException"/> if the server does not
    /// return a 401 with a parsable Bearer challenge.
    /// </summary>
    public async Task<Uri> GetAuthorityAsync(Uri environmentUrl, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(environmentUrl);
        var probe = new Uri(environmentUrl, WhoAmIPath);
        using var response = await _http.GetAsync(probe, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException(
                $"Expected HTTP 401 from {probe} but got {(int)response.StatusCode} {response.StatusCode}.");
        }

        foreach (var header in response.Headers.WwwAuthenticate)
        {
            if (TryParseAuthorizationUri(header, out var authority))
            {
                _logger.LogDebug("Resolved authority {Authority} from WWW-Authenticate challenge at {Probe}.", authority, probe);
                return authority;
            }
        }

        throw new InvalidOperationException(
            $"Dataverse challenge at {probe} did not include an authorization_uri parameter.");
    }

    internal static bool TryParseAuthorizationUri(AuthenticationHeaderValue header, out Uri authority)
    {
        authority = null!;
        if (!string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(header.Parameter))
            return false;

        foreach (var segment in header.Parameter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = segment.IndexOf('=');
            if (eq <= 0) continue;
            var key = segment[..eq].Trim();
            var value = segment[(eq + 1)..].Trim().Trim('"');
            if (!string.Equals(key, "authorization_uri", StringComparison.OrdinalIgnoreCase))
                continue;
            if (Uri.TryCreate(value, UriKind.Absolute, out var parsed))
            {
                authority = parsed;
                return true;
            }
        }
        return false;
    }

    internal void DisposeOwnedHttpClient()
    {
        if (_disposeHttp) _http.Dispose();
    }
}
