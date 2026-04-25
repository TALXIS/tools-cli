using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control;

/// <summary>
/// Backend for the Dataverse Organization table. This is the same surface
/// that PAC CLI's <c>env list-settings</c> / <c>env update-settings</c>
/// targets: a single row in the <c>organization</c> entity with ~854 columns.
/// </summary>
internal sealed class OrganizationTableSettingsBackend : ISettingsBackend
{
    private const string OrganizationsPath = "api/data/v9.2/organizations";

    /// <summary>
    /// Prefixes used by OData annotations that should be stripped from
    /// the result set (unless the caller explicitly requested them).
    /// </summary>
    private static readonly string[] AnnotationPrefixes = { "@odata.", "@OData.", "@Microsoft." };

    private readonly IAccessTokenService _tokens;
    private readonly IHttpClientFactoryWrapper _httpFactory;

    public OrganizationTableSettingsBackend(
        IAccessTokenService tokens,
        IHttpClientFactoryWrapper? httpFactory = null)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _httpFactory = httpFactory ?? DefaultHttpClientFactoryWrapper.Instance;
    }

    public async Task<IReadOnlyList<EnvironmentSetting>> ListAsync(
        Connection connection, Credential credential, Guid environmentId, CancellationToken ct)
    {
        var envUri = GetEnvironmentUri(connection);
        var (body, _) = await GetOrganizationsAsync(connection, credential, envUri, ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (!root.TryGetProperty("value", out var valueArray)
            || valueArray.ValueKind != JsonValueKind.Array
            || valueArray.GetArrayLength() == 0)
            return Array.Empty<EnvironmentSetting>();

        return ParseOrganizationRow(valueArray[0]);
    }

    public async Task<bool> TryUpdateAsync(
        Connection connection, Credential credential, Guid environmentId,
        string settingName, string value, CancellationToken ct)
    {
        var envUri = GetEnvironmentUri(connection);

        // GET the org row to validate the setting name exists and retrieve the organizationid.
        var (body, _) = await GetOrganizationsAsync(connection, credential, envUri, ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (!root.TryGetProperty("value", out var valueArray)
            || valueArray.ValueKind != JsonValueKind.Array
            || valueArray.GetArrayLength() == 0)
            return false;

        var orgRow = valueArray[0];

        // Check if the setting name exists in the organization row (case-insensitive).
        bool found = false;
        foreach (var prop in orgRow.EnumerateObject())
        {
            if (prop.Name.Equals(settingName, StringComparison.OrdinalIgnoreCase))
            {
                found = true;
                break;
            }
        }

        if (!found)
            return false;

        // Extract organizationid for the PATCH URL.
        if (!orgRow.TryGetProperty("organizationid", out var orgIdProp)
            || orgIdProp.ValueKind != JsonValueKind.String
            || !Guid.TryParse(orgIdProp.GetString(), out var orgId))
            throw new InvalidOperationException("Could not read 'organizationid' from the organization row.");

        // PATCH the setting.
        var payload = new JsonObject { [settingName] = EnvironmentSettingsClient.CoerceValue(value) };
        var token = await AcquireTokenAsync(connection, credential, envUri, ct).ConfigureAwait(false);

        using var http = _httpFactory.Create();
        var patchUrl = new Uri(envUri, $"{OrganizationsPath}({orgId})");
        using var request = new HttpRequestMessage(HttpMethod.Patch, patchUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var respBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Organization table PATCH failed ({(int)response.StatusCode}): {Truncate(respBody, 500)}");
        }

        return true;
    }

    /// <summary>
    /// Fetches the entire organization row with formatted value annotations,
    /// mirroring PAC CLI's approach.
    /// </summary>
    private async Task<(string Body, string Token)> GetOrganizationsAsync(
        Connection connection, Credential credential, Uri envUri, CancellationToken ct)
    {
        var token = await AcquireTokenAsync(connection, credential, envUri, ct).ConfigureAwait(false);

        using var http = _httpFactory.Create();
        var url = new Uri(envUri, OrganizationsPath);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("Prefer", "odata.include-annotations=\"OData.Community.Display.V1.FormattedValue\"");

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Organization table GET failed ({(int)response.StatusCode}): {Truncate(body, 500)}");

        return (body, token);
    }

    /// <summary>
    /// Parses a single organization JSON row into <see cref="EnvironmentSetting"/>
    /// records. Strips OData annotation keys and prefers formatted values
    /// (e.g. "Yes"/"No") over raw booleans, matching PAC CLI behaviour.
    /// </summary>
    internal static IReadOnlyList<EnvironmentSetting> ParseOrganizationRow(JsonElement orgRow)
    {
        // First pass: collect formatted values so we can prefer them.
        const string formattedSuffix = "@OData.Community.Display.V1.FormattedValue";
        var formattedValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in orgRow.EnumerateObject())
        {
            if (prop.Name.EndsWith(formattedSuffix, StringComparison.OrdinalIgnoreCase)
                && prop.Value.ValueKind == JsonValueKind.String)
            {
                var baseName = prop.Name[..^formattedSuffix.Length];
                formattedValues[baseName] = prop.Value.GetString()!;
            }
        }

        // Second pass: build settings, skipping annotation keys.
        var settings = new List<EnvironmentSetting>();
        foreach (var prop in orgRow.EnumerateObject())
        {
            if (IsAnnotationKey(prop.Name))
                continue;

            // Use the formatted value if available, otherwise the raw value.
            object? value;
            if (formattedValues.TryGetValue(prop.Name, out var formatted))
            {
                value = formatted;
            }
            else
            {
                value = prop.Value.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Number => prop.Value.TryGetInt32(out var i) ? i : prop.Value.GetDouble(),
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Null => null,
                    _ => prop.Value.GetRawText(),
                };
            }

            settings.Add(new EnvironmentSetting(prop.Name, value));
        }

        return settings;
    }

    private async Task<string> AcquireTokenAsync(
        Connection connection, Credential credential, Uri envUri, CancellationToken ct)
    {
        return await _tokens.AcquireForResourceAsync(connection, credential, envUri, ct)
            .ConfigureAwait(false);
    }

    private static Uri GetEnvironmentUri(Connection connection)
    {
        if (string.IsNullOrWhiteSpace(connection.EnvironmentUrl)
            || !Uri.TryCreate(connection.EnvironmentUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException(
                $"Connection '{connection.Id}' is missing a valid EnvironmentUrl for Organization table access.");
        // Ensure trailing slash for relative URI resolution.
        return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
    }

    private static bool IsAnnotationKey(string key)
    {
        foreach (var prefix in AnnotationPrefixes)
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || key.Contains(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s[..max] + "...");
}
