namespace TALXIS.CLI.Features.Environment.Component.Browse;

/// <summary>
/// URL builders for the Power Apps canvas app player (<c>apps.powerapps.com/play</c>).
/// Supports screen navigation, custom parameters, and hidden navbar.
/// </summary>
public static class CanvasAppUrls
{
    private const string Base = "https://apps.powerapps.com/play";

    /// <summary>
    /// Open a canvas app in the Power Apps player.
    /// </summary>
    public static Uri Play(Guid environmentId, Guid appId, string? tenantId,
        string? screenName = null, IDictionary<string, string>? customParams = null, bool hideNavbar = false)
    {
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(tenantId))
            qs.Add($"tenantId={Uri.EscapeDataString(tenantId)}");
        if (!string.IsNullOrWhiteSpace(screenName))
            qs.Add($"screenName={Uri.EscapeDataString(screenName)}");
        if (hideNavbar)
            qs.Add("hidenavbar=true");
        if (customParams != null)
            foreach (var (key, value) in customParams)
                qs.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");

        var query = qs.Count > 0 ? "?" + string.Join("&", qs) : "";
        return new Uri($"{Base}/e/{environmentId}/a/{appId}{query}");
    }
}
