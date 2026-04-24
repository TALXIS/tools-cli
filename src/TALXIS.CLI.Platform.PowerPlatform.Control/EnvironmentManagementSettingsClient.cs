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
public sealed class EnvironmentManagementSettingsClient
{
    private const string ApiVersion = "2022-03-01-preview";
    private static readonly Uri PowerPlatformApiAudience = new("https://api.powerplatform.com/");

    private readonly IAccessTokenService _tokens;
    private readonly IHttpClientFactoryWrapper _httpFactory;

    public EnvironmentManagementSettingsClient(
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
    public async Task<IReadOnlyList<EnvironmentManagementSetting>> ListAsync(
        Connection connection,
        Credential credential,
        Guid environmentId,
        string? selectFilter,
        CancellationToken ct)
    {
        var baseUri = GetBaseUri(connection.Cloud ?? CloudInstance.Public);
        var url = $"{baseUri}environmentmanagement/environments/{environmentId}/settings?api-version={ApiVersion}";
        if (!string.IsNullOrWhiteSpace(selectFilter))
            url += $"&$select={Uri.EscapeDataString(selectFilter)}";

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
    /// PATCH a single environment management setting.
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

        using var http = _httpFactory.Create();
        using var request = new HttpRequestMessage(HttpMethod.Patch, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Environment management settings PATCH failed ({(int)response.StatusCode} {response.ReasonPhrase}): {Truncate(body, 500)}");
    }

    /// <summary>
    /// Parses the <c>GetEnvironmentManagementSettingResponse</c> envelope and
    /// flattens the first <c>objectResult</c> item into name-value pairs.
    /// Skips <c>id</c> and <c>tenantId</c> since they are identifiers, not settings.
    /// </summary>
    internal static IReadOnlyList<EnvironmentManagementSetting> ParseListResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("objectResult", out var objectResult)
            || objectResult.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<EnvironmentManagementSetting>();
        }

        var settings = new List<EnvironmentManagementSetting>();
        foreach (var item in objectResult.EnumerateArray())
        {
            foreach (var prop in item.EnumerateObject())
            {
                // Skip envelope identifiers — they are not settings.
                if (prop.Name is "id" or "tenantId")
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

                settings.Add(new EnvironmentManagementSetting(prop.Name, value));
            }
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
