using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control;

/// <summary>
/// Low-level HTTP client for the Power Platform environment management
/// settings API (<c>api.powerplatform.com/environmentmanagement</c>).
/// Stateless — callers supply connection, credential, and environment ID.
/// </summary>
public sealed class EnvironmentSettingsClient
{
    private const string ApiVersion = "1";
    private static readonly Uri PowerPlatformApiAudience = new("https://api.powerplatform.com/");

    /// <summary>
    /// Known environment management setting names from the Power Platform API
    /// schema. The GET endpoint requires <c>$select</c> to return setting
    /// values — without it only the <c>Id</c> is returned.
    /// </summary>
    internal static readonly string[] KnownSettingNames =
    {
        "allowedIpRangeForStorageAccessSignatures",
        "copilotStudio_CodeInterpreter",
        "copilotStudio_ComputerUseAppAllowlist",
        "copilotStudio_ComputerUseCredentialsAllowed",
        "copilotStudio_ComputerUseSharedMachines",
        "copilotStudio_ComputerUseWebAllowlist",
        "copilotStudio_ConnectedAgents",
        "copilotStudio_ConversationAuditLoggingEnabled",
        "d365CustomerService_AIAgents",
        "d365CustomerService_Copilot",
        "enableIpBasedStorageAccessSignatureRule",
        "ipBasedStorageAccessSignatureMode",
        "loggingEnabledForIpBasedStorageAccessSignature",
        "PowerApps_AllowCodeApps",
        "powerApps_ChartVisualization",
        "powerApps_CopilotChat",
        "powerApps_EnableFormInsights",
        "powerApps_FormPredictAutomatic",
        "powerApps_FormPredictSmartPaste",
        "powerApps_NLSearch",
        "powerPages_AllowIntelligentFormsCopilotForSites",
        "powerPages_AllowListSummaryCopilotForSites",
        "powerPages_AllowMakerCopilotsForExistingSites",
        "powerPages_AllowMakerCopilotsForNewSites",
        "powerPages_AllowNonProdPublicSites",
        "powerPages_AllowNonProdPublicSites_Exemptions",
        "powerPages_AllowProDevCopilotsForEnvironment",
        "powerPages_AllowProDevCopilotsForSites",
        "powerPages_AllowSearchSummaryCopilotForSites",
        "powerPages_AllowSiteCopilotForSites",
        "powerPages_AllowSummarizationAPICopilotForSites",
    };

    private readonly IAccessTokenService _tokens;
    private readonly IHttpClientFactoryWrapper _httpFactory;

    public EnvironmentSettingsClient(
        IAccessTokenService tokens,
        IHttpClientFactoryWrapper? httpFactory = null)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _httpFactory = httpFactory ?? DefaultHttpClientFactoryWrapper.Instance;
    }

    /// <summary>
    /// GET environment management settings. Returns flattened name-value
    /// pairs from the first item in the <c>objectResult</c> array.
    /// </summary>
    public async Task<IReadOnlyList<EnvironmentSetting>> ListAsync(
        Connection connection,
        Credential credential,
        Guid environmentId,
        string? selectFilter,
        CancellationToken ct)
    {
        var baseUri = GetBaseUri(connection.Cloud ?? CloudInstance.Public);

        // The API requires $select to return setting values — without it
        // only the Id field is included in the response. When no explicit
        // select filter is provided, request all known setting names.
        var select = string.IsNullOrWhiteSpace(selectFilter)
            ? string.Join(",", KnownSettingNames)
            : selectFilter;
        var url = $"{baseUri}environmentmanagement/environments/{environmentId}/settings?api-version={ApiVersion}&$select={Uri.EscapeDataString(select)}";

        var token = await _tokens.AcquireForResourceAsync(connection, credential, PowerPlatformApiAudience, ct)
            .ConfigureAwait(false);

        using var http = _httpFactory.Create();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Environment management settings GET failed ({(int)response.StatusCode} {response.ReasonPhrase}): {Truncate(body, 500)}");

        return ParseListResponse(body);
    }

    /// <summary>
    /// Creates or updates an environment management setting. Tries PATCH
    /// (update) first; if the settings row doesn't exist yet (404), falls
    /// back to POST (create) + PATCH. This mirrors the Power Platform API
    /// contract where settings must be explicitly created before they can
    /// be updated.
    /// </summary>
    public async Task UpdateAsync(
        Connection connection,
        Credential credential,
        Guid environmentId,
        string settingName,
        string value,
        CancellationToken ct)
    {
        var baseUri = GetBaseUri(connection.Cloud ?? CloudInstance.Public);
        var url = $"{baseUri}environmentmanagement/environments/{environmentId}/settings?api-version={ApiVersion}";

        var token = await _tokens.AcquireForResourceAsync(connection, credential, PowerPlatformApiAudience, ct)
            .ConfigureAwait(false);

        var payload = new JsonObject { [settingName] = CoerceValue(value) };
        var payloadString = payload.ToJsonString();

        using var http = _httpFactory.Create();

        // Try PATCH first (most common case — settings row already exists).
        var patchResponse = await SendSettingsRequestAsync(http, HttpMethod.Patch, url, token, payloadString, ct)
            .ConfigureAwait(false);

        if (patchResponse.IsSuccess)
            return;

        if (patchResponse.StatusCode == 404)
        {
            // Settings row doesn't exist yet — create it with POST, then PATCH.
            var postResponse = await SendSettingsRequestAsync(http, HttpMethod.Post, url, token, payloadString, ct)
                .ConfigureAwait(false);

            if (!postResponse.IsSuccess)
                throw new InvalidOperationException(
                    $"Environment management settings create (POST) failed ({postResponse.StatusCode}): {Truncate(postResponse.Body, 500)}");

            // POST creates the row; PATCH sets the actual value.
            var retryPatch = await SendSettingsRequestAsync(http, HttpMethod.Patch, url, token, payloadString, ct)
                .ConfigureAwait(false);

            if (!retryPatch.IsSuccess)
                throw new InvalidOperationException(
                    $"Environment management settings update (PATCH) failed after create ({retryPatch.StatusCode}): {Truncate(retryPatch.Body, 500)}");

            return;
        }

        throw new InvalidOperationException(
            $"Environment management settings update failed ({patchResponse.StatusCode}): {Truncate(patchResponse.Body, 500)}");
    }

    private static async Task<SettingsResponse> SendSettingsRequestAsync(
        HttpClient http, HttpMethod method, string url, string token, string payload, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        return new SettingsResponse(response.IsSuccessStatusCode, (int)response.StatusCode, body);
    }

    private sealed record SettingsResponse(bool IsSuccess, int StatusCode, string Body);

    /// <summary>
    /// Parses the <c>GetEnvironmentSettingResponse</c> envelope and
    /// flattens the first <c>objectResult</c> item into name-value pairs.
    /// Skips <c>id</c> and <c>tenantId</c> since they are identifiers, not settings.
    /// </summary>
    internal static IReadOnlyList<EnvironmentSetting> ParseListResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("objectResult", out var objectResult)
            || objectResult.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<EnvironmentSetting>();
        }

        var items = objectResult.EnumerateArray();
        if (!items.MoveNext() || items.Current.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<EnvironmentSetting>();
        }

        // The API contract for this method is to flatten settings from the first
        // objectResult entry only. Ignore any additional array entries to avoid
        // merging unrelated properties into a single settings collection.
        var firstItem = items.Current;
        var settings = new List<EnvironmentSetting>();
        foreach (var prop in firstItem.EnumerateObject())
        {
            // Skip envelope identifiers — they are not settings.
            // The API returns these with varying casing (e.g. "Id" vs "id").
            if (prop.Name.Equals("id", StringComparison.OrdinalIgnoreCase)
                || prop.Name.Equals("tenantId", StringComparison.OrdinalIgnoreCase))
                continue;

            object? value = prop.Value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => prop.Value.TryGetInt32(out var i) ? i : prop.Value.GetDouble(),
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Null => null,
                _ => prop.Value.GetRawText(),
            };

            settings.Add(new EnvironmentSetting(prop.Name, value));
        }

        return settings;
    }

    /// <summary>
    /// Auto-coerces a string value to the appropriate <see cref="JsonNode"/>
    /// type: <c>true</c>/<c>false</c> → bool, numeric → int, else string.
    /// </summary>
    internal static JsonNode CoerceValue(string value)
    {
        if (bool.TryParse(value, out var b))
            return JsonValue.Create(b);
        if (int.TryParse(value, out var i))
            return JsonValue.Create(i);
        return JsonValue.Create(value)!;
    }

    private static Uri GetBaseUri(CloudInstance cloud) => cloud switch
    {
        CloudInstance.Public or CloudInstance.Gcc => new Uri("https://api.powerplatform.com/"),
        _ => throw new NotSupportedException(
            $"Environment management settings API is not wired for cloud '{cloud}' in this release."),
    };

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s[..max] + "...");
}
