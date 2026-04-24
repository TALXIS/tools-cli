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

    private const int MaxDefaultNameLength = 64;

    /// <summary>
    /// Derives a default profile/connection name from the Power Platform
    /// environment display name plus the URL host's first DNS label. When
    /// no display name is available, falls back to the first DNS label only.
    /// Names are lowercased, non-alphanumeric runs collapse to <c>-</c>, and
    /// the result is capped at 64 characters.
    /// </summary>
    public static string? DeriveDefaultName(string? environmentDisplayName, string? url)
    {
        var hostSlug = DeriveHostSlug(url);
        var displaySlug = Slugify(environmentDisplayName);

        if (string.IsNullOrEmpty(displaySlug))
            return hostSlug;
        if (string.IsNullOrEmpty(hostSlug))
            return displaySlug;
        if (string.Equals(displaySlug, hostSlug, StringComparison.Ordinal))
            return displaySlug;
        if (displaySlug.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Contains(hostSlug, StringComparer.Ordinal))
            return TruncateSlug(displaySlug);

        var maxDisplayLength = MaxDefaultNameLength - hostSlug.Length - 1;
        if (maxDisplayLength <= 0)
            return TruncateSlug(hostSlug);

        var displayPrefix = displaySlug.Length <= maxDisplayLength
            ? displaySlug
            : TrimTrailingDash(displaySlug[..maxDisplayLength]);

        return string.IsNullOrEmpty(displayPrefix)
            ? TruncateSlug(hostSlug)
            : $"{displayPrefix}-{hostSlug}";
    }

    /// <summary>
    /// Derives a default profile/connection name from the URL host's
    /// first DNS label (<c>https://contoso.crm4.dynamics.com/</c> →
    /// <c>contoso</c>). Lowercased; non-alphanumeric runs collapsed to
    /// <c>-</c>; trimmed to 64 chars. Returns <c>null</c> if the URL
    /// is malformed or yields no usable label.
    /// </summary>
    public static string? DeriveDefaultName(string? url)
        => DeriveDefaultName(environmentDisplayName: null, url);

    private static string? DeriveHostSlug(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;

        var host = uri.Host;
        if (string.IsNullOrEmpty(host)) return null;
        var dot = host.IndexOf('.');
        var head = dot > 0 ? host[..dot] : host;

        return Slugify(head);
    }

    private static string? Slugify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var buf = new System.Text.StringBuilder(value.Length);
        var lastDash = true;
        foreach (var c in value.Trim().ToLowerInvariant())
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

        return TruncateSlug(buf.ToString());
    }

    private static string? TruncateSlug(string? slug)
    {
        if (string.IsNullOrEmpty(slug)) return null;

        var trimmed = slug.Length > MaxDefaultNameLength
            ? slug[..MaxDefaultNameLength]
            : slug;

        trimmed = TrimTrailingDash(trimmed);
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string TrimTrailingDash(string value)
    {
        var trimmed = value;
        while (trimmed.Length > 0 && trimmed[^1] == '-')
            trimmed = trimmed[..^1];

        return trimmed;
    }
}
