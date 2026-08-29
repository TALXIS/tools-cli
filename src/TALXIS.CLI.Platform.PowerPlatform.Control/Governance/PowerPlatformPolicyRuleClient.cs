using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control.PowerPlatformRbac;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Governance;

/// <summary>
/// Thin authenticated client over the rule-based-policy CRUD and assignment
/// endpoints under <c>api.powerplatform.com/governance/ruleBasedPolicies</c>
/// (api-version 2024-10-01, confirmed from the official REST API reference -
/// see <see cref="IPowerPlatformPolicyRuleClient"/> remarks for the two
/// operations this API does not currently expose).
/// </summary>
public sealed class PowerPlatformPolicyRuleClient : IPowerPlatformPolicyRuleClient
{
    private const string ApiVersion = "2024-10-01";
    private static readonly Uri PowerPlatformApiAudience = new("https://api.powerplatform.com/");

    private readonly IAccessTokenService _tokens;
    private readonly IHttpClientFactoryWrapper _httpFactory;

    public PowerPlatformPolicyRuleClient(
        IAccessTokenService tokens,
        IHttpClientFactoryWrapper? httpFactory = null)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _httpFactory = httpFactory ?? DefaultHttpClientFactoryWrapper.Instance;
    }

    public async Task<IReadOnlyList<PowerPlatformPolicy>> ListAsync(
        Connection connection, Credential credential, CancellationToken ct)
    {
        var initialRequestUri = BuildUri(connection, $"governance/ruleBasedPolicies?api-version={ApiVersion}");

        return await ODataPagingSupport.FetchAllPagesAsync(
            initialRequestUri,
            (requestUri, pageCt) => SendForBodyAsync(connection, credential, HttpMethod.Get, requestUri, jsonBody: null, pageCt),
            item => TryParsePolicy(item, out var policy) ? policy : null,
            "Rule-based policy payload did not contain a 'value' array.",
            ct).ConfigureAwait(false);
    }

    public async Task<PowerPlatformPolicy?> GetAsync(
        Connection connection, Credential credential, Guid policyId, CancellationToken ct)
    {
        var body = await SendForBodyAsync(
            connection,
            credential,
            HttpMethod.Get,
            BuildUri(connection, $"governance/ruleBasedPolicies/{policyId}?api-version={ApiVersion}"),
            jsonBody: null,
            ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(body))
            return null;

        using var document = JsonDocument.Parse(body);
        return TryParsePolicy(document.RootElement, out var policy) ? policy : null;
    }

    public async Task<PowerPlatformPolicy> CreateAsync(
        Connection connection, Credential credential, PowerPlatformPolicyCreateOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Name);

        var requestBody = new
        {
            name = options.Name,
            ruleSets = options.RuleSets.Select(ToWireRuleSet).ToArray(),
        };

        var responseBody = await SendForBodyAsync(
            connection,
            credential,
            HttpMethod.Post,
            BuildUri(connection, $"governance/ruleBasedPolicies?api-version={ApiVersion}"),
            requestBody,
            ct).ConfigureAwait(false);

        using var document = JsonDocument.Parse(responseBody);
        return TryParsePolicy(document.RootElement, out var policy)
            ? policy
            : throw new InvalidOperationException("Rule-based policy creation response could not be parsed.");
    }

    public async Task<PowerPlatformPolicy> UpdateAsync(
        Connection connection, Credential credential, Guid policyId, PowerPlatformPolicyPatchOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);

        var requestBody = new Dictionary<string, object?>();
        if (options.Name is not null)
            requestBody["name"] = options.Name;
        if (options.RuleSets is not null)
            requestBody["ruleSets"] = options.RuleSets.Select(ToWireRuleSet).ToArray();

        var responseBody = await SendForBodyAsync(
            connection,
            credential,
            HttpMethod.Patch,
            BuildUri(connection, $"governance/ruleBasedPolicies/{policyId}?api-version={ApiVersion}"),
            requestBody,
            ct).ConfigureAwait(false);

        using var document = JsonDocument.Parse(responseBody);
        return TryParsePolicy(document.RootElement, out var policy)
            ? policy
            : throw new InvalidOperationException("Rule-based policy update response could not be parsed.");
    }

    public async Task<PowerPlatformPolicy> RemoveRuleSetAsync(
        Connection connection, Credential credential, Guid policyId, string ruleSetId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleSetId);

        // The removeRule endpoint identifies which rule set(s) to remove by
        // matching entries in the same PolicyRequest {name, ruleSets} shape
        // used everywhere else - only "id" is required to identify a match.
        var requestBody = new { ruleSets = new[] { new { id = ruleSetId } } };

        var responseBody = await SendForBodyAsync(
            connection,
            credential,
            HttpMethod.Patch,
            BuildUri(connection, $"governance/ruleBasedPolicies/{policyId}/removeRule?api-version={ApiVersion}"),
            requestBody,
            ct).ConfigureAwait(false);

        using var document = JsonDocument.Parse(responseBody);
        return TryParsePolicy(document.RootElement, out var policy)
            ? policy
            : throw new InvalidOperationException("Rule-based policy 'remove rule' response could not be parsed.");
    }

    public Task<PowerPlatformPolicyAssignment> AssignToEnvironmentGroupAsync(
        Connection connection, Credential credential, Guid policyId, Guid environmentGroupId,
        IReadOnlyList<PowerPlatformPolicyAssignmentOverride>? overrides, CancellationToken ct)
        => AssignCoreAsync(
            connection, credential,
            $"governance/ruleBasedPolicies/{policyId}/environmentGroups/{environmentGroupId}/assignments?api-version={ApiVersion}",
            overrides, ct);

    public Task<PowerPlatformPolicyAssignment> AssignToEnvironmentAsync(
        Connection connection, Credential credential, Guid policyId, Guid environmentId,
        IReadOnlyList<PowerPlatformPolicyAssignmentOverride>? overrides, CancellationToken ct)
        => AssignCoreAsync(
            connection, credential,
            $"governance/ruleBasedPolicies/{policyId}/environments/{environmentId}/assignments?api-version={ApiVersion}",
            overrides, ct);

    private async Task<PowerPlatformPolicyAssignment> AssignCoreAsync(
        Connection connection, Credential credential, string relativePath,
        IReadOnlyList<PowerPlatformPolicyAssignmentOverride>? overrides, CancellationToken ct)
    {
        var requestBody = new
        {
            assignmentOverrides = (overrides ?? Array.Empty<PowerPlatformPolicyAssignmentOverride>())
                .Select(o => new
                {
                    behaviorType = o.BehaviorType.ToString(),
                    resourceId = o.ResourceId,
                    resourceType = o.ResourceType.ToString(),
                })
                .ToArray(),
        };

        var responseBody = await SendForBodyAsync(
            connection, credential, HttpMethod.Post, BuildUri(connection, relativePath), requestBody, ct)
            .ConfigureAwait(false);

        using var document = JsonDocument.Parse(responseBody);
        return TryParseAssignment(document.RootElement, out var assignment)
            ? assignment
            : throw new InvalidOperationException("Rule-based policy assignment response could not be parsed.");
    }

    public async Task<IReadOnlyList<PowerPlatformPolicyAssignment>> ListAssignmentsAsync(
        Connection connection, Credential credential,
        Guid? policyId, Guid? environmentGroupId, Guid? environmentId, CancellationToken ct)
    {
        var filterCount = (policyId.HasValue ? 1 : 0) + (environmentGroupId.HasValue ? 1 : 0) + (environmentId.HasValue ? 1 : 0);
        if (filterCount > 1)
        {
            throw new ArgumentException(
                "Specify at most one of policyId, environmentGroupId, or environmentId when listing policy assignments.");
        }

        var relativePath = policyId.HasValue
            ? $"governance/ruleBasedPolicies/{policyId}/assignments?includeRuleSetCounts=true&api-version={ApiVersion}"
            : environmentGroupId.HasValue
                ? $"governance/ruleBasedPolicies/environmentGroups/{environmentGroupId}/assignments?includeRuleSetCounts=true&api-version={ApiVersion}"
                : environmentId.HasValue
                    ? $"governance/ruleBasedPolicies/environments/{environmentId}/assignments?includeRuleSetCounts=true&api-version={ApiVersion}"
                    : $"governance/ruleBasedPolicies/assignments?includeRuleSetCounts=true&api-version={ApiVersion}";

        var initialRequestUri = BuildUri(connection, relativePath);

        return await ODataPagingSupport.FetchAllPagesAsync(
            initialRequestUri,
            (requestUri, pageCt) => SendForBodyAsync(connection, credential, HttpMethod.Get, requestUri, jsonBody: null, pageCt),
            item => TryParseAssignment(item, out var assignment) ? assignment : null,
            "Rule-based policy assignment payload did not contain a 'value' array.",
            ct).ConfigureAwait(false);
    }

    private async Task<string> SendForBodyAsync(
        Connection connection, Credential credential, HttpMethod method, Uri requestUri, object? jsonBody, CancellationToken ct)
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
            request.Content = new StringContent(JsonSerializer.Serialize(jsonBody), Encoding.UTF8, "application/json");
        }

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Rule-based policy request failed ({(int)response.StatusCode} {response.ReasonPhrase}) against '{requestUri}': {PowerPlatformRbacClient.Truncate(responseBody, 500)}");
        }

        return responseBody;
    }

    private static Uri BuildUri(Connection connection, string relativePath)
        => new(GetBaseUri(connection.Cloud ?? CloudInstance.Public), relativePath);

    private static Uri GetBaseUri(CloudInstance cloud) => cloud switch
    {
        CloudInstance.Public or CloudInstance.Gcc => new Uri("https://api.powerplatform.com/"),
        _ => throw new NotSupportedException(
            $"Rule-based policies are not wired for cloud '{cloud}' in this release."),
    };

    private static object? ToWireRuleSet(PowerPlatformPolicyRuleSet ruleSet) => new
    {
        id = ruleSet.Id,
        version = ruleSet.Version,
        inputs = string.IsNullOrWhiteSpace(ruleSet.InputsJson)
            ? (JsonElement?)null
            : JsonSerializer.Deserialize<JsonElement>(ruleSet.InputsJson),
    };

    private static bool TryParsePolicy(JsonElement item, out PowerPlatformPolicy policy)
    {
        policy = null!;

        if (!TryReadGuid(item, "id", out var id) || !TryReadString(item, "name", out var name))
            return false;

        var ruleSets = new List<PowerPlatformPolicyRuleSet>();
        if (item.TryGetProperty("ruleSets", out var ruleSetsElement) && ruleSetsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var ruleSetItem in ruleSetsElement.EnumerateArray())
            {
                if (TryParseRuleSet(ruleSetItem, out var ruleSet))
                    ruleSets.Add(ruleSet);
            }
        }

        policy = new PowerPlatformPolicy(
            id,
            name,
            TryReadOptionalString(item, "tenantId"),
            TryReadOptionalDateTimeOffset(item, "lastModified"),
            item.TryGetProperty("ruleSetCount", out var countElement) && countElement.TryGetInt32(out var count) ? count : ruleSets.Count,
            ruleSets);
        return true;
    }

    private static bool TryParseRuleSet(JsonElement item, out PowerPlatformPolicyRuleSet ruleSet)
    {
        ruleSet = null!;

        if (!TryReadString(item, "id", out var id))
            return false;

        var version = TryReadOptionalString(item, "version") ?? "1.0";
        var inputsJson = item.TryGetProperty("inputs", out var inputs) ? inputs.GetRawText() : "{}";

        ruleSet = new PowerPlatformPolicyRuleSet(id, version, inputsJson);
        return true;
    }

    private static bool TryParseAssignment(JsonElement item, out PowerPlatformPolicyAssignment assignment)
    {
        assignment = null!;

        if (!TryReadGuid(item, "policyId", out var policyId) || !TryReadGuid(item, "resourceId", out var resourceId))
            return false;

        var resourceType = TryReadString(item, "resourceType", out var resourceTypeRaw)
            && Enum.TryParse<PowerPlatformPolicyAssignmentResourceType>(resourceTypeRaw, ignoreCase: true, out var parsedType)
            ? parsedType
            : PowerPlatformPolicyAssignmentResourceType.NotSpecified;

        assignment = new PowerPlatformPolicyAssignment(
            policyId,
            resourceId,
            resourceType,
            item.TryGetProperty("ruleSetCount", out var countElement) && countElement.TryGetInt32(out var count) ? count : 0,
            TryReadOptionalString(item, "tenantId"));
        return true;
    }

    private static bool TryReadGuid(JsonElement element, string propertyName, out Guid value)
    {
        value = default;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            return false;

        return Guid.TryParse(property.GetString(), out value);
    }

    private static bool TryReadString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
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
}
