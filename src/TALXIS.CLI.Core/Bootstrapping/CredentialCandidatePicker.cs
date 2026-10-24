using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Core.Bootstrapping;

/// <summary>
/// Shared logic used by both <see cref="InteractiveCredentialBootstrapper"/>
/// and <see cref="DeviceCodeCredentialBootstrapper"/> to find and pick the
/// best existing credential for a sign-in result.
/// </summary>
internal static class CredentialCandidatePicker
{
    /// <summary>
    /// Selects the best credential from <paramref name="candidates"/> by
    /// applying the following preference ordering:
    /// <list type="number">
    ///   <item>Credential whose <c>Id</c> matches <paramref name="explicitAlias"/> (if provided).</item>
    ///   <item>Credential whose <c>Id</c> matches <paramref name="upn"/>.</item>
    ///   <item>Alphabetically first by <c>Id</c>.</item>
    /// </list>
    /// Returns <c>null</c> when the set is empty.
    /// </summary>
    public static Credential? PickPreferred(
        IEnumerable<Credential> candidates,
        string? explicitAlias,
        string upn)
    {
        var list = candidates.ToList();
        if (list.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(explicitAlias))
        {
            var explicitMatch = list.FirstOrDefault(c =>
                string.Equals(c.Id, explicitAlias.Trim(), StringComparison.OrdinalIgnoreCase));
            if (explicitMatch is not null)
                return explicitMatch;
        }

        return list
            .OrderBy(c => string.Equals(c.Id, upn, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    /// <summary>
    /// Finds the stored credential a sign-in should update instead of creating a
    /// duplicate. Browser and device-code credentials for the same account are
    /// interchangeable, so a re-login with either flow reuses either kind.
    /// </summary>
    public static async Task<Credential?> FindExistingAsync(
        ICredentialStore store,
        InteractiveLoginResult result,
        CloudInstance cloud,
        string? explicitAlias,
        CancellationToken ct)
    {
        var credentials = await store.ListAsync(ct).ConfigureAwait(false);
        var candidates = credentials
            .Where(c => c.Kind is CredentialKind.InteractiveBrowser or CredentialKind.DeviceCode)
            .Where(c => string.Equals(c.TenantId, result.TenantId, StringComparison.OrdinalIgnoreCase))
            .Where(c => c.Cloud == cloud)
            .Where(c =>
                string.IsNullOrWhiteSpace(result.ApplicationId)
                || string.IsNullOrWhiteSpace(c.ApplicationId)
                || string.Equals(c.ApplicationId, result.ApplicationId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!string.IsNullOrWhiteSpace(result.AccountId))
        {
            var exact = PickPreferred(
                candidates.Where(c =>
                    string.Equals(c.InteractiveAccountId, result.AccountId, StringComparison.OrdinalIgnoreCase)),
                explicitAlias,
                result.Upn);
            if (exact is not null)
                return exact;
        }

        // Older credentials predate persisted account ids: match on the stored
        // UPN, then on the legacy "UPN as id".
        return PickPreferred(
            candidates.Where(c =>
                string.Equals(c.InteractiveUpn, result.Upn, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Id, result.Upn, StringComparison.OrdinalIgnoreCase)),
            explicitAlias,
            result.Upn);
    }
}
