using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse;

/// <summary>
/// Real <see cref="IDataverseLiveChecker"/>: acquires an access token via
/// <see cref="IDataverseAccessTokenService"/> and calls the Dataverse
/// <c>WhoAmI</c> Web API endpoint to confirm the identity and the endpoint
/// are both live. Replaces the previous stub implementation.
/// </summary>
/// <remarks>
/// <para>
/// The check uses the Web API (<c>/api/data/v9.2/WhoAmI</c>) rather than the
/// SDK <c>ServiceClient</c>. This keeps live-validate cheap (a single HTTP
/// round trip, no SOAP metadata fetch) and independent of the Dataverse SDK
/// caching. The response shape is stable across v9.x.
/// </para>
/// <para>
/// HTTP failures surface as <see cref="InvalidOperationException"/> with a
/// remediation hint that includes the status code and response body so the
/// <c>profile validate</c> command can show the user enough to act on.
/// </para>
/// </remarks>
public sealed class DataverseLiveChecker : IDataverseLiveChecker
{
    private const string WhoAmIRelativePath = "/api/data/v9.2/WhoAmI";

    private readonly IDataverseAccessTokenService _tokens;
    private readonly IHttpClientFactoryWrapper _httpFactory;
    private readonly ILogger<DataverseLiveChecker> _logger;

    public DataverseLiveChecker(
        IDataverseAccessTokenService tokens,
        IHttpClientFactoryWrapper? httpFactory = null,
        ILogger<DataverseLiveChecker>? logger = null)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _httpFactory = httpFactory ?? DefaultHttpClientFactoryWrapper.Instance;
        _logger = logger ?? NullLogger<DataverseLiveChecker>.Instance;
    }

    public async Task<DataverseLiveCheckResult> CheckAsync(TALXIS.CLI.Core.Model.Connection connection, Credential credential, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(credential);

        if (string.IsNullOrWhiteSpace(connection.EnvironmentUrl))
            throw new InvalidOperationException($"Dataverse connection '{connection.Id}' is missing EnvironmentUrl.");
        if (!Uri.TryCreate(connection.EnvironmentUrl, UriKind.Absolute, out var envUri))
            throw new InvalidOperationException($"Dataverse connection '{connection.Id}' EnvironmentUrl '{connection.EnvironmentUrl}' is not a valid absolute URI.");

        var token = await _tokens.AcquireAsync(connection, credential, ct).ConfigureAwait(false);

        var whoAmI = new Uri(envUri, WhoAmIRelativePath);
        using var http = _httpFactory.Create();
        using var req = new HttpRequestMessage(HttpMethod.Get, whoAmI);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Add("OData-MaxVersion", "4.0");
        req.Headers.Add("OData-Version", "4.0");

        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Dataverse WhoAmI failed ({(int)resp.StatusCode} {resp.ReasonPhrase}) for '{envUri}': {Truncate(body, 500)}");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var userId = ReadGuid(root, "UserId");
        var businessUnitId = ReadGuid(root, "BusinessUnitId");
        var organizationId = ReadGuid(root, "OrganizationId");

        _logger.LogDebug(
            "WhoAmI OK for '{EnvUrl}' (userId={UserId}, orgId={OrgId}).",
            envUri, userId, organizationId);

        return new DataverseLiveCheckResult(userId, businessUnitId, organizationId);
    }

    private static Guid ReadGuid(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var element) || element.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException($"WhoAmI response missing '{property}' field.");
        if (!Guid.TryParse(element.GetString(), out var guid))
            throw new InvalidOperationException($"WhoAmI '{property}' is not a valid GUID: '{element.GetString()}'.");
        return guid;
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s[..max] + "...");
}

/// <summary>
/// Thin seam so tests can swap the <see cref="HttpClient"/> used by
/// <see cref="DataverseLiveChecker"/> without standing up a real HTTP stack.
/// </summary>
public interface IHttpClientFactoryWrapper
{
    HttpClient Create();
}

internal sealed class DefaultHttpClientFactoryWrapper : IHttpClientFactoryWrapper
{
    public static readonly DefaultHttpClientFactoryWrapper Instance = new();
    public HttpClient Create() => new();
}
