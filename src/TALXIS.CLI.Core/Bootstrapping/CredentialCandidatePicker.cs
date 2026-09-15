using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Core.Bootstrapping;

/// <summary>
/// Shared deterministic credential-selection logic used by both
/// <see cref="InteractiveCredentialBootstrapper"/> and
/// <see cref="DeviceCodeCredentialBootstrapper"/> to pick the best
/// existing credential from a candidate set.
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
}
