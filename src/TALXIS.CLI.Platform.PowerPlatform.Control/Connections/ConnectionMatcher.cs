using TALXIS.CLI.Core.Deployment;

namespace TALXIS.CLI.Platform.PowerPlatform.Control;

/// <summary>Pure matching of settings-file connection references against a target environment's connection ids.</summary>
public static class ConnectionMatcher
{
    /// <summary>Normalizes a connection id for comparison (ids come with or without hyphens): strip hyphens, lowercase.</summary>
    public static string Normalize(string id) => (id ?? string.Empty).Trim().Replace("-", string.Empty).ToLowerInvariant();

    /// <summary>Returns a descriptor for each reference whose connection id is absent. References without an id are ignored.</summary>
    public static IReadOnlyList<string> FindMissing(
        IReadOnlyList<ConnectionReferenceSetting> references,
        IEnumerable<string> existingConnectionIds)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(existingConnectionIds);

        var existing = existingConnectionIds.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = new List<string>();
        foreach (var reference in references)
        {
            if (string.IsNullOrWhiteSpace(reference.ConnectionId))
                continue;

            if (!existing.Contains(Normalize(reference.ConnectionId)))
                missing.Add($"'{reference.LogicalName}' -> connection {reference.ConnectionId}");
        }

        return missing;
    }
}
