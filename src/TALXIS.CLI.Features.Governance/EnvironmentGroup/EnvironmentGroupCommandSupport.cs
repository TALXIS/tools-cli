using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Features.Governance.EnvironmentGroup;

/// <summary>
/// Shared profile-resolution and lookup helpers for
/// <c>txc governance environment-group</c> commands. Environment groups are
/// a tenant-wide resource (like <c>txc security</c>'s resources), so this
/// resolves a (Connection, Credential) context the same way
/// <c>SecurityPrincipalCommandSupport</c> does — no active-environment
/// resolution is involved here (that concept only applies to the
/// <c>--environment</c> scope flag on <c>txc security</c> RBAC commands).
/// </summary>
internal static class EnvironmentGroupCommandSupport
{
    internal static Task<ResolvedProfileContext> ResolveContextAsync(string? profile, CancellationToken ct)
        => TxcServices.Get<IConfigurationResolver>().ResolveAsync(profile, ct);

    /// <summary>
    /// Resolves an environment group by GUID id, or by exact/unique
    /// case-insensitive display-name match when a non-GUID value is passed.
    /// Throws <see cref="InvalidOperationException"/> with a clear message
    /// when the value matches zero or more than one group by name.
    /// </summary>
    internal static async Task<PowerPlatformEnvironmentGroup> ResolveAsync(
        Connection connection, Credential credential, string environmentGroup, CancellationToken ct)
    {
        var client = TxcServices.Get<IPowerPlatformEnvironmentGroupClient>();

        if (Guid.TryParse(environmentGroup, out var id))
        {
            var byId = await client.GetAsync(connection, credential, id, ct).ConfigureAwait(false);
            if (byId is null)
                throw new ArgumentException($"No environment group was found with id '{environmentGroup}'.");

            return byId;
        }

        var all = await client.ListAsync(connection, credential, ct).ConfigureAwait(false);
        var matches = all.Where(g => string.Equals(g.DisplayName, environmentGroup, StringComparison.OrdinalIgnoreCase)).ToList();

        if (matches.Count == 0)
            throw new ArgumentException($"No environment group was found with display name '{environmentGroup}'.");

        if (matches.Count > 1)
        {
            throw new ArgumentException(
                $"Multiple environment groups match display name '{environmentGroup}': " +
                string.Join(", ", matches.Select(m => m.Id)) +
                ". Specify the environment group id instead.");
        }

        return matches[0];
    }
}
