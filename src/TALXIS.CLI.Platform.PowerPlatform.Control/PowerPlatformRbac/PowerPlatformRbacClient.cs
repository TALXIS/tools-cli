using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.PowerPlatformRbac;

internal sealed record PowerPlatformRbacRoleAssignment(
    string RoleAssignmentId,
    string Scope,
    PowerPlatformPrincipalType PrincipalType,
    Guid PrincipalObjectId,
    Guid RoleDefinitionId,
    PowerPlatformPrincipalType? CreatedByPrincipalType,
    Guid? CreatedByPrincipalObjectId,
    DateTimeOffset? CreatedOn,
    DateTimeOffset? ExpiresOn);

/// <summary>
/// Thin authenticated client over the tenant-scoped Power Platform RBAC
/// endpoints under <c>api.powerplatform.com/authorization</c>.
/// </summary>
public sealed class PowerPlatformRbacClient
{
    private const string ApiVersion = "2024-10-01";
    private static readonly Uri PowerPlatformApiAudience = new("https://api.powerplatform.com/");

    private readonly IAccessTokenService _tokens;
    private readonly IHttpClientFactoryWrapper _httpFactory;

    public PowerPlatformRbacClient(
        IAccessTokenService tokens,
        IHttpClientFactoryWrapper? httpFactory = null)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _httpFactory = httpFactory ?? DefaultHttpClientFactoryWrapper.Instance;
    }

    public async Task<IReadOnlyList<PowerPlatformRoleDefinition>> ListRoleDefinitionsAsync(
        Connection connection,
        Credential credential,
        CancellationToken ct)
    {
        var initialRequestUri = BuildUri(connection, $"authorization/roleDefinitions?api-version={ApiVersion}");

        return await ODataPagingSupport.FetchAllPagesAsync(
            initialRequestUri,
            (requestUri, pageCt) => SendForBodyAsync(connection, credential, HttpMethod.Get, requestUri, jsonBody: null, pageCt),
            item => TryParseRoleDefinition(item, out var role) ? role : null,
            "Power Platform RBAC role definition payload did not contain a 'value' array.",
            ct).ConfigureAwait(false);
    }

    internal async Task<IReadOnlyList<PowerPlatformRbacRoleAssignment>> ListTenantRoleAssignmentsAsync(
        Connection connection,
        Credential credential,
        CancellationToken ct)
    {
        var initialRequestUri = BuildUri(
            connection,
            $"authorization/roleAssignments?api-version={ApiVersion}&scope={Uri.EscapeDataString(BuildTenantScope(connection))}");
        return await ListRoleAssignmentsCoreAsync(connection, credential, initialRequestUri, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists role assignments for an environment group via the dedicated
    /// <c>authorization/environmentGroups/{id}/roleAssignments</c> route.
    /// Unlike the tenant-level endpoint, environment-group role assignments
    /// are NOT reachable through the generic <c>authorization/roleAssignments
    /// ?scope=</c> query filter - Microsoft exposes a dedicated path-routed
    /// resource for this scope instead (confirmed via official REST API
    /// reference docs, see PowerPlatformEnvironmentGroupRoleClient remarks).
    /// </summary>
    internal async Task<IReadOnlyList<PowerPlatformRbacRoleAssignment>> ListEnvironmentGroupRoleAssignmentsAsync(
        Connection connection,
        Credential credential,
        Guid environmentGroupId,
        CancellationToken ct)
    {
        var initialRequestUri = BuildUri(
            connection,
            $"authorization/environmentGroups/{environmentGroupId}/roleAssignments?api-version={ApiVersion}");
        return await ListRoleAssignmentsCoreAsync(connection, credential, initialRequestUri, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<PowerPlatformRbacRoleAssignment>> ListRoleAssignmentsCoreAsync(
        Connection connection,
        Credential credential,
        Uri initialRequestUri,
        CancellationToken ct)
        => await ODataPagingSupport.FetchAllPagesAsync(
            initialRequestUri,
            (requestUri, pageCt) => SendForBodyAsync(connection, credential, HttpMethod.Get, requestUri, jsonBody: null, pageCt),
            item => TryParseRoleAssignment(item, out var assignment) ? assignment : null,
            "Power Platform RBAC role assignment payload did not contain a 'value' array.",
            ct).ConfigureAwait(false);

    internal async Task<PowerPlatformRbacRoleAssignment?> AddTenantRoleAssignmentAsync(
        Connection connection,
        Credential credential,
        PowerPlatformPrincipalType principalType,
        Guid principalObjectId,
        Guid roleDefinitionId,
        CancellationToken ct)
    {
        // The generic tenant-level endpoint requires the target scope in the
        // request body (confirmed via official RBAC role-assignment tutorial).
        var body = new
        {
            principalObjectId,
            principalType = principalType.ToString(),
            roleDefinitionId,
            scope = BuildTenantScope(connection),
        };

        return await AddRoleAssignmentCoreAsync(
            connection, credential, BuildUri(connection, $"authorization/roleAssignments?api-version={ApiVersion}"), body, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Adds a role assignment for an environment group via the dedicated
    /// <c>authorization/environmentGroups/{id}/roleAssignments</c> route.
    /// Unlike the tenant-level request body, no <c>scope</c> field is sent -
    /// the scope is implicit from the environment group id in the URL path
    /// (confirmed via official REST API reference docs).
    /// </summary>
    internal async Task<PowerPlatformRbacRoleAssignment?> AddEnvironmentGroupRoleAssignmentAsync(
        Connection connection,
        Credential credential,
        Guid environmentGroupId,
        PowerPlatformPrincipalType principalType,
        Guid principalObjectId,
        Guid roleDefinitionId,
        CancellationToken ct)
    {
        var body = new
        {
            principalObjectId,
            principalType = principalType.ToString(),
            roleDefinitionId,
        };

        return await AddRoleAssignmentCoreAsync(
            connection,
            credential,
            BuildUri(connection, $"authorization/environmentGroups/{environmentGroupId}/roleAssignments?api-version={ApiVersion}"),
            body,
            ct).ConfigureAwait(false);
    }

    private async Task<PowerPlatformRbacRoleAssignment?> AddRoleAssignmentCoreAsync(
        Connection connection,
        Credential credential,
        Uri requestUri,
        object body,
        CancellationToken ct)
    {
        var responseBody = await SendForBodyAsync(connection, credential, HttpMethod.Post, requestUri, body, ct)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(responseBody))
            return null;

        using var document = JsonDocument.Parse(responseBody);
        return TryParseRoleAssignment(document.RootElement, out var assignment) ? assignment : null;
    }

    public async Task RemoveTenantRoleAssignmentAsync(
        Connection connection,
        Credential credential,
        string roleAssignmentId,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleAssignmentId);

        await SendForBodyAsync(
            connection,
            credential,
            HttpMethod.Delete,
            BuildUri(connection, $"authorization/roleAssignments/{roleAssignmentId}?api-version={ApiVersion}"),
            jsonBody: null,
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes a role assignment from an environment group via the dedicated
    /// <c>authorization/environmentGroups/{id}/roleAssignments/{roleAssignmentId}</c>
    /// route (confirmed via official REST API reference docs).
    /// </summary>
    internal async Task RemoveEnvironmentGroupRoleAssignmentAsync(
        Connection connection,
        Credential credential,
        Guid environmentGroupId,
        string roleAssignmentId,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleAssignmentId);

        await SendForBodyAsync(
            connection,
            credential,
            HttpMethod.Delete,
            BuildUri(connection, $"authorization/environmentGroups/{environmentGroupId}/roleAssignments/{roleAssignmentId}?api-version={ApiVersion}"),
            jsonBody: null,
            ct).ConfigureAwait(false);
    }

    private async Task<string> SendForBodyAsync(
        Connection connection,
        Credential credential,
        HttpMethod method,
        Uri requestUri,
        object? jsonBody,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(credential);

        var token = await _tokens.AcquireForResourceAsync(connection, credential, PowerPlatformApiAudience, ct)
            .ConfigureAwait(false);

        using var http = _httpFactory.Create();
        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (jsonBody is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(jsonBody),
                Encoding.UTF8,
                "application/json");
        }

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Power Platform RBAC request failed ({(int)response.StatusCode} {response.ReasonPhrase}) against '{requestUri}': {Truncate(body, 500)}");
        }

        return body;
    }

    private static Uri BuildUri(Connection connection, string relativePath)
        => new(GetBaseUri(connection.Cloud ?? CloudInstance.Public), relativePath);

    private static Uri GetBaseUri(CloudInstance cloud) => cloud switch
    {
        CloudInstance.Public or CloudInstance.Gcc => new Uri("https://api.powerplatform.com/"),
        _ => throw new NotSupportedException(
            $"Power Platform RBAC is not wired for cloud '{cloud}' in this release."),
    };

    internal static string BuildTenantScope(Connection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (string.IsNullOrWhiteSpace(connection.TenantId))
        {
            throw new InvalidOperationException(
                $"Connection '{connection.Id}' does not carry a TenantId required for tenant-scoped role operations.");
        }

        return "/tenants/" + connection.TenantId.Trim();
    }

    private static bool TryParseRoleDefinition(JsonElement item, out PowerPlatformRoleDefinition role)
    {
        role = null!;

        if (!TryReadGuid(item, "roleDefinitionId", out var roleDefinitionId)
            || !TryReadString(item, "roleDefinitionName", out var roleDefinitionName))
            return false;

        var assignableScopes = new List<string>();
        if (item.TryGetProperty("assignableScopes", out var scopes) && scopes.ValueKind == JsonValueKind.Array)
        {
            foreach (var scope in scopes.EnumerateArray())
            {
                if (scope.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(scope.GetString()))
                    assignableScopes.Add(scope.GetString()!.Trim());
            }
        }

        role = new PowerPlatformRoleDefinition(
            roleDefinitionId,
            roleDefinitionName,
            TryReadOptionalString(item, "description"),
            assignableScopes);
        return true;
    }

    private static bool TryParseRoleAssignment(JsonElement item, out PowerPlatformRbacRoleAssignment assignment)
    {
        assignment = null!;

        if (!TryReadString(item, "roleAssignmentId", out var roleAssignmentId)
            || !TryReadString(item, "scope", out var scope)
            || !TryReadPrincipalType(item, "principalType", out var principalType)
            || !TryReadGuid(item, "principalObjectId", out var principalObjectId)
            || !TryReadGuid(item, "roleDefinitionId", out var roleDefinitionId))
            return false;

        PowerPlatformPrincipalType? createdByPrincipalType = TryReadPrincipalType(item, "createdByPrincipalType", out var createdByType)
            ? createdByType
            : null;
        Guid? createdByPrincipalObjectId = TryReadGuid(item, "createdByPrincipalObjectId", out var createdByObjectId)
            ? createdByObjectId
            : null;

        assignment = new PowerPlatformRbacRoleAssignment(
            roleAssignmentId,
            scope,
            principalType,
            principalObjectId,
            roleDefinitionId,
            createdByPrincipalType,
            createdByPrincipalObjectId,
            TryReadOptionalDateTimeOffset(item, "createdOn"),
            TryReadOptionalDateTimeOffset(item, "expiresOn"));
        return true;
    }

    private static bool TryReadGuid(JsonElement element, string propertyName, out Guid value)
    {
        value = default;
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
            return false;

        return Guid.TryParse(property.GetString(), out value);
    }

    private static bool TryReadString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
            return false;

        var raw = property.GetString();
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        value = raw.Trim();
        return true;
    }

    private static string? TryReadOptionalString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()?.Trim()
            : null;

    private static DateTimeOffset? TryReadOptionalDateTimeOffset(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property)
           && property.ValueKind == JsonValueKind.String
           && DateTimeOffset.TryParse(property.GetString(), out var value)
            ? value
            : null;

    private static bool TryReadPrincipalType(JsonElement element, string propertyName, out PowerPlatformPrincipalType principalType)
    {
        principalType = default;
        return TryReadString(element, propertyName, out var raw)
            && Enum.TryParse(raw, ignoreCase: true, out principalType);
    }

    internal static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s[..max] + "...");
}
