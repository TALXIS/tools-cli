using TALXIS.CLI.Core.Abstractions;

namespace TALXIS.CLI.Core.Bootstrapping;

/// <summary>
/// Derives a collision-free credential alias from a UPN. Extracted from
/// <c>AuthLoginCliCommand</c> so the one-liner bootstrap
/// (<c>profile create --url</c>) can reuse the exact same rule set — if
/// two commands can create credentials, they must agree on the default
/// alias or users end up with silent duplicates.
/// </summary>
public static class CredentialAliasResolver
{
    /// <summary>
    /// Derives an alias from the UPN. Falls back to appending the UPN's
    /// tenant-domain short name, then numeric suffixes, until an unused
    /// alias is found.
    /// </summary>
    public static async Task<string> ResolveForUpnAsync(
        ICredentialStore store, string upn, CancellationToken ct)
    {
        if (store is null) throw new ArgumentNullException(nameof(store));
        if (string.IsNullOrWhiteSpace(upn)) throw new ArgumentException("UPN must not be empty.", nameof(upn));

        var slug = upn.Trim().ToLowerInvariant();
        if (await store.GetAsync(slug, ct).ConfigureAwait(false) is null)
            return slug;

        var shortName = ExtractTenantShortName(upn);
        if (!string.IsNullOrEmpty(shortName))
        {
            var combined = $"{slug}-{shortName}";
            if (await store.GetAsync(combined, ct).ConfigureAwait(false) is null)
                return combined;
        }

        // Numeric fallback caps at 99 — past that the user should pass an explicit alias.
        for (var i = 2; i < 100; i++)
        {
            var candidate = $"{slug}-{i}";
            if (await store.GetAsync(candidate, ct).ConfigureAwait(false) is null)
                return candidate;
        }

        throw new InvalidOperationException(
            $"Cannot derive a unique alias for '{upn}' — pass an explicit alias.");
    }

    /// <summary>
    /// Returns the first domain label from the UPN in lowercase, e.g.
    /// <c>tomas@contoso.com</c> → <c>contoso</c>. Returns <c>null</c>
    /// when the UPN has no usable domain portion.
    /// </summary>
    public static string? ExtractTenantShortName(string upn)
    {
        if (string.IsNullOrWhiteSpace(upn)) return null;
        var at = upn.IndexOf('@');
        if (at < 0 || at == upn.Length - 1) return null;

        var domain = upn[(at + 1)..];
        var dot = domain.IndexOf('.');
        var head = dot > 0 ? domain[..dot] : domain;
        head = head.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(head) ? null : head;
    }

    /// <summary>
    /// Returns <paramref name="preferredBase"/> if free, otherwise the
    /// first numeric-suffixed variant (<c>-2</c>, <c>-3</c>, …) that
    /// passes <paramref name="exists"/>. Caps at 99 to avoid infinite
    /// loops on misbehaving stores.
    /// </summary>
    public static async Task<string> ResolveFreeNameAsync(
        string preferredBase,
        Func<string, CancellationToken, Task<bool>> exists,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(preferredBase))
            throw new ArgumentException("Name base must not be empty.", nameof(preferredBase));
        if (exists is null) throw new ArgumentNullException(nameof(exists));

        if (!await exists(preferredBase, ct).ConfigureAwait(false))
            return preferredBase;

        for (var i = 2; i < 100; i++)
        {
            var candidate = $"{preferredBase}-{i}";
            if (!await exists(candidate, ct).ConfigureAwait(false))
                return candidate;
        }

        throw new InvalidOperationException(
            $"Cannot derive a unique name starting from '{preferredBase}' — pass an explicit name.");
    }
}
