using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Graph;

public sealed record GraphServicePrincipal(Guid Id, Guid? AppId, string? DisplayName);

public sealed record GraphUser(Guid Id, string? DisplayName, string? UserPrincipalName);

/// <summary>
/// Small authenticated client for read-only Microsoft Graph directory lookups
/// used by <c>txc security</c> commands. This client deliberately supports only
/// the GET endpoints required by the feature: service principals and users.
/// Entra groups are intentionally never looked up through this client - see
/// the remarks on <see cref="SecurityRoleResolver"/>'s group resolution for why.
/// </summary>
public sealed class MicrosoftGraphClient
{
    private static readonly Uri GraphAudience = new("https://graph.microsoft.com/");
    private static readonly Uri GraphBaseUri = new("https://graph.microsoft.com/v1.0/");

    private readonly IAccessTokenService _tokens;
    private readonly IHttpClientFactoryWrapper _httpFactory;

    public MicrosoftGraphClient(
        IAccessTokenService tokens,
        IHttpClientFactoryWrapper? httpFactory = null)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _httpFactory = httpFactory ?? DefaultHttpClientFactoryWrapper.Instance;
    }

    public Task<IReadOnlyList<GraphServicePrincipal>> ListServicePrincipalsAsync(
        Connection connection,
        Credential credential,
        string? filter,
        int? top,
        CancellationToken ct)
        => GetPagedAsync(
            connection,
            credential,
            BuildCollectionUri(
                "servicePrincipals",
                filter,
                top,
                "$select=id,appId,displayName"),
            ParseServicePrincipal,
            "service principals",
            "Application.Read.All",
            ct);

    public Task<IReadOnlyList<GraphUser>> ListUsersAsync(
        Connection connection,
        Credential credential,
        string? filter,
        int? top,
        CancellationToken ct)
        => GetPagedAsync(
            connection,
            credential,
            BuildCollectionUri(
                "users",
                filter,
                top,
                "$select=id,displayName,userPrincipalName"),
            ParseUser,
            "users",
            "User.Read.All",
            ct);

    private async Task<IReadOnlyList<T>> GetPagedAsync<T>(
        Connection connection,
        Credential credential,
        Uri initialRequestUri,
        Func<JsonElement, T?> projector,
        string entityName,
        string likelyMissingPermission,
        CancellationToken ct)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(credential);

        var token = await _tokens.AcquireForResourceAsync(connection, credential, GraphAudience, ct)
            .ConfigureAwait(false);

        using var http = _httpFactory.Create();

        var results = await ODataPagingSupport.FetchAllPagesAsync(
            initialRequestUri,
            async (requestUri, pageCt) =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, pageCt)
                    .ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(pageCt).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.Forbidden)
                    throw CreateForbiddenException(credential, entityName, likelyMissingPermission, body);

                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(
                        $"Microsoft Graph {entityName} lookup failed ({(int)response.StatusCode} {response.ReasonPhrase}): {Truncate(body, 500)}");
                }

                return body;
            },
            projector,
            $"Microsoft Graph {entityName} payload did not contain a 'value' array.",
            ct).ConfigureAwait(false);

        return results;
    }

    private static Uri BuildCollectionUri(string relativePath, string? filter, int? top, string select)
    {
        var queryParts = new List<string> { select };

        if (!string.IsNullOrWhiteSpace(filter))
            queryParts.Add("$filter=" + Uri.EscapeDataString(filter.Trim()));

        if (top is > 0)
            queryParts.Add("$top=" + top.Value);

        return new Uri(GraphBaseUri, relativePath + "?" + string.Join("&", queryParts));
    }

    private static GraphServicePrincipal? ParseServicePrincipal(JsonElement item)
    {
        if (!TryReadGuid(item, "id", out var id))
            return null;

        TryReadGuid(item, "appId", out var appId);
        return new GraphServicePrincipal(id, appId, TryReadOptionalString(item, "displayName"));
    }

    private static GraphUser? ParseUser(JsonElement item)
    {
        if (!TryReadGuid(item, "id", out var id))
            return null;

        return new GraphUser(
            id,
            TryReadOptionalString(item, "displayName"),
            TryReadOptionalString(item, "userPrincipalName"));
    }

    private static bool TryReadGuid(JsonElement element, string propertyName, out Guid value)
    {
        value = default;
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
            return false;

        return Guid.TryParse(property.GetString(), out value);
    }

    private static string? TryReadOptionalString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()?.Trim()
            : null;

    private static InvalidOperationException CreateForbiddenException(
        Credential credential,
        string entityName,
        string likelyMissingPermission,
        string body)
    {
        var detail = Truncate(body, 300);
        if (IsApplicationCredential(credential.Kind))
        {
            return new InvalidOperationException(
                $"Microsoft Graph {entityName} lookup failed with 403 Forbidden. Credential '{credential.Id}' uses {credential.Kind} and likely lacks the admin-consented Microsoft Graph application permission '{likelyMissingPermission}'. Grant '{likelyMissingPermission}' to the app registration and approve tenant-wide admin consent, then retry. Response: {detail}");
        }

        return new InvalidOperationException(
            $"Microsoft Graph {entityName} lookup failed with 403 Forbidden. The active delegated identity is not allowed to read {entityName} in this tenant. If you switch to a service-principal credential, ensure the Microsoft Graph application permission '{likelyMissingPermission}' has tenant-wide admin consent. Response: {detail}");
    }

    private static bool IsApplicationCredential(CredentialKind kind)
        => kind is CredentialKind.ClientSecret
            or CredentialKind.ClientCertificate
            or CredentialKind.ManagedIdentity
            or CredentialKind.WorkloadIdentityFederation;

    internal static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s[..max] + "...");
}
