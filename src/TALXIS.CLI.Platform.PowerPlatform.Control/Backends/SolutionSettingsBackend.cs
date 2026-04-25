using System.Net.Http.Headers;
using System.Text.Json;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control;

/// <summary>
/// Backend for Dataverse solution-based settings (Setting definitions).
/// Uses the <c>RetrieveSettingList()</c> Web API function to list
/// environment-level settings. Read-only in v1 — updates require creating
/// Setting Environment Value solution components.
/// </summary>
internal sealed class SolutionSettingsBackend : ISettingsBackend
{
    private const string RetrieveSettingListPath = "api/data/v9.2/RetrieveSettingList()";

    private readonly IAccessTokenService _tokens;
    private readonly IHttpClientFactoryWrapper _httpFactory;

    public SolutionSettingsBackend(
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
        var token = await _tokens.AcquireForResourceAsync(connection, credential, envUri, ct)
            .ConfigureAwait(false);

        using var http = _httpFactory.Create();
        var url = new Uri(envUri, RetrieveSettingListPath);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"RetrieveSettingList() failed ({(int)response.StatusCode}): {Truncate(body, 500)}");

        return ParseSettingList(body);
    }

    /// <summary>
    /// Solution settings are read-only in v1. Always returns <c>false</c>.
    /// </summary>
    public Task<bool> TryUpdateAsync(
        Connection connection, Credential credential, Guid environmentId,
        string settingName, string value, CancellationToken ct)
    {
        return Task.FromResult(false);
    }

    /// <summary>
    /// Parses the <c>SettingDetailCollection</c> from the
    /// <c>RetrieveSettingList()</c> response.
    /// </summary>
    internal static IReadOnlyList<EnvironmentSetting> ParseSettingList(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("SettingDetailCollection", out var collection)
            || collection.ValueKind != JsonValueKind.Array)
            return Array.Empty<EnvironmentSetting>();

        var settings = new List<EnvironmentSetting>();
        foreach (var item in collection.EnumerateArray())
        {
            if (!item.TryGetProperty("Name", out var nameProp)
                || nameProp.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(nameProp.GetString()))
                continue;

            object? value = null;
            if (item.TryGetProperty("Value", out var valueProp))
            {
                value = valueProp.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Number => valueProp.TryGetInt32(out var i) ? i : valueProp.GetDouble(),
                    JsonValueKind.String => valueProp.GetString(),
                    JsonValueKind.Null => null,
                    _ => valueProp.GetRawText(),
                };
            }

            settings.Add(new EnvironmentSetting(nameProp.GetString()!, value));
        }

        return settings;
    }

    private static Uri GetEnvironmentUri(Connection connection)
    {
        if (string.IsNullOrWhiteSpace(connection.EnvironmentUrl)
            || !Uri.TryCreate(connection.EnvironmentUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException(
                $"Connection '{connection.Id}' is missing a valid EnvironmentUrl.");
        return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s[..max] + "...");
}
