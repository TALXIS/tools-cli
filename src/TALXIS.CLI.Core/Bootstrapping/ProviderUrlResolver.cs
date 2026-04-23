using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Core.Bootstrapping;

/// <summary>
/// Infers a <see cref="ProviderKind"/> from a service URL host, so the
/// one-liner <c>profile create --url</c> can accept a single URL and
/// figure out the rest. Table-driven — future providers (jira, devops)
/// plug in with a single <see cref="HostSuffixRule"/> entry.
/// </summary>
/// <remarks>
/// Rules are evaluated top-down. First match wins. An unknown host
/// returns <c>null</c> and the caller is expected to demand
/// <c>--provider</c> with an actionable error that lists the known
/// host suffixes.
/// </remarks>
public static class ProviderUrlResolver
{
    public sealed record HostSuffixRule(string Suffix, ProviderKind Provider);

    /// <summary>
    /// Commercial + sovereign Dataverse host suffixes. Gov / DoD / China
    /// endpoints share the same provider — the sovereign <see cref="CloudInstance"/>
    /// is resolved separately by the Dataverse authority map.
    /// </summary>
    public static IReadOnlyList<HostSuffixRule> DefaultRules { get; } = new List<HostSuffixRule>
    {
        new(".dynamics.com",            ProviderKind.Dataverse),
        new(".crm.microsoftdynamics.us", ProviderKind.Dataverse),
        new(".crm.appsplatform.us",      ProviderKind.Dataverse),
        new(".dynamics.cn",              ProviderKind.Dataverse),
        // future providers land here — one line each:
        // new(".atlassian.net",          ProviderKind.Jira),
    };

    public sealed record InferenceResult(ProviderKind? Provider, string? Error);

    /// <summary>
    /// Parses <paramref name="url"/> and returns the matching provider
    /// (or <c>null</c>+error for unknown hosts / malformed URLs).
    /// </summary>
    public static InferenceResult Infer(string? url, IReadOnlyList<HostSuffixRule>? rules = null)
    {
        if (string.IsNullOrWhiteSpace(url))
            return new InferenceResult(null, "URL must not be empty.");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return new InferenceResult(null, $"'{url}' is not an absolute http(s) URL.");

        var host = uri.Host.ToLowerInvariant();
        foreach (var rule in rules ?? DefaultRules)
        {
            if (host.EndsWith(rule.Suffix, StringComparison.Ordinal))
                return new InferenceResult(rule.Provider, null);
        }

        var known = string.Join(", ", (rules ?? DefaultRules).Select(r => $"*{r.Suffix}"));
        return new InferenceResult(null,
            $"Cannot infer provider from host '{uri.Host}'. Pass --provider explicitly. Known host suffixes: {known}.");
    }

    /// <summary>
    /// Derives a default profile/connection name from the URL host's
    /// first DNS label (<c>https://contoso.crm4.dynamics.com/</c> →
    /// <c>contoso</c>). Lowercased; non-alphanumeric runs collapsed to
    /// <c>-</c>; trimmed to 64 chars. Returns <c>null</c> if the URL
    /// is malformed or yields no usable label.
    /// </summary>
    public static string? DeriveDefaultName(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;

        var host = uri.Host;
        if (string.IsNullOrEmpty(host)) return null;
        var dot = host.IndexOf('.');
        var head = dot > 0 ? host[..dot] : host;

        var buf = new System.Text.StringBuilder(head.Length);
        var lastDash = true;
        foreach (var c in head.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                buf.Append(c);
                lastDash = false;
            }
            else if (!lastDash && buf.Length > 0)
            {
                buf.Append('-');
                lastDash = true;
            }
        }
        while (buf.Length > 0 && buf[^1] == '-')
            buf.Length--;

        if (buf.Length == 0) return null;
        if (buf.Length > 64) buf.Length = 64;
        while (buf.Length > 0 && buf[^1] == '-')
            buf.Length--;
        return buf.Length == 0 ? null : buf.ToString();
    }
}
