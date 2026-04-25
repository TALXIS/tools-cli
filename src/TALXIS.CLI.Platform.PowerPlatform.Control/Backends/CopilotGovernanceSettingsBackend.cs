using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control;

/// <summary>
/// Backend for the Copilot governance per-environment settings API.
/// Endpoint pattern: <c>{envId}.environment.api.powerplatform.com/copilotgovernance/settings/{name}</c>.
/// These control AI model access toggles (e.g. <c>PowerPlatform_Anthropic</c>).
/// </summary>
internal sealed class CopilotGovernanceSettingsBackend : ISettingsBackend
{
    private static readonly Uri PowerPlatformApiAudience = new("https://api.powerplatform.com/");

    /// <summary>
    /// Known copilot governance setting names. The API uses per-setting
    /// endpoints with no "list all" capability, so we must enumerate
    /// known names explicitly. This list is expected to grow over time.
    /// </summary>
    internal static readonly string[] KnownSettingNames =
    {
        "PowerPlatform_Anthropic",
    };

    private readonly IAccessTokenService _tokens;
    private readonly IHttpClientFactoryWrapper _httpFactory;

    public CopilotGovernanceSettingsBackend(
        IAccessTokenService tokens,
        IHttpClientFactoryWrapper? httpFactory = null)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _httpFactory = httpFactory ?? DefaultHttpClientFactoryWrapper.Instance;
    }

    public async Task<IReadOnlyList<EnvironmentSetting>> ListAsync(
        Connection connection, Credential credential, Guid environmentId, CancellationToken ct)
    {
        var baseUri = BuildEnvironmentApiBaseUri(environmentId, connection.Cloud ?? CloudInstance.Public);
        var token = await _tokens.AcquireForResourceAsync(connection, credential, PowerPlatformApiAudience, ct)
            .ConfigureAwait(false);

        var settings = new List<EnvironmentSetting>();

        // Query each known setting individually — no "list all" endpoint exists.
        foreach (var name in KnownSettingNames)
        {
            try
            {
                var setting = await GetSettingAsync(baseUri, token, name, ct).ConfigureAwait(false);
                if (setting is not null)
                    settings.Add(setting);
            }
            catch
            {
                // Swallow per-setting errors — the setting may not be provisioned
                // for this environment. Continue with the next one.
            }
        }

        return settings;
    }

    public async Task<bool> TryUpdateAsync(
        Connection connection, Credential credential, Guid environmentId,
        string settingName, string value, CancellationToken ct)
    {
        if (!KnownSettingNames.Any(n => n.Equals(settingName, StringComparison.OrdinalIgnoreCase)))
            return false;

        var baseUri = BuildEnvironmentApiBaseUri(environmentId, connection.Cloud ?? CloudInstance.Public);
        var token = await _tokens.AcquireForResourceAsync(connection, credential, PowerPlatformApiAudience, ct)
            .ConfigureAwait(false);

        var url = $"{baseUri}copilotgovernance/settings/{Uri.EscapeDataString(settingName)}?api-version=1";

        // The copilot governance API expects { "value": <bool> } for boolean settings.
        var payload = new JsonObject { ["value"] = EnvironmentSettingsClient.CoerceValue(value) };

        using var http = _httpFactory.Create();
        using var request = new HttpRequestMessage(HttpMethod.Put, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Copilot governance settings update failed ({(int)response.StatusCode}): {Truncate(body, 500)}");
        }

        return true;
    }

    private async Task<EnvironmentSetting?> GetSettingAsync(
        string baseUri, string token, string settingName, CancellationToken ct)
    {
        var url = $"{baseUri}copilotgovernance/settings/{Uri.EscapeDataString(settingName)}?api-version=1";

        using var http = _httpFactory.Create();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            return null;

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (!root.TryGetProperty("value", out var valueProp))
            return null;

        object? value = valueProp.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => valueProp.TryGetInt32(out var i) ? i : valueProp.GetDouble(),
            JsonValueKind.String => valueProp.GetString(),
            _ => valueProp.GetRawText(),
        };

        // Use settingName from the response if available for accurate casing.
        var name = root.TryGetProperty("settingName", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
            ? nameProp.GetString()!
            : settingName;

        return new EnvironmentSetting(name, value);
    }

    /// <summary>
    /// Builds the environment-scoped API base URL. The hostname format is
    /// <c>{envId-no-dashes-with-dot}.environment.api.powerplatform.com</c>
    /// where the GUID is stripped of dashes and a dot is inserted before
    /// the last two characters.
    /// </summary>
    internal static string BuildEnvironmentApiBaseUri(Guid environmentId, CloudInstance cloud)
    {
        var noDashes = environmentId.ToString("N"); // 32 hex chars, no dashes
        var hostPrefix = noDashes[..^2] + "." + noDashes[^2..];

        var domain = cloud switch
        {
            CloudInstance.Public or CloudInstance.Gcc => "environment.api.powerplatform.com",
            _ => throw new NotSupportedException(
                $"Copilot governance API is not wired for cloud '{cloud}' in this release."),
        };

        return $"https://{hostPrefix}.{domain}/";
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s[..max] + "...");
}
