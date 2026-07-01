using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Platform.Dataverse.Data;

/// <summary>
/// Pure name-to-timezone matching so <c>user-settings set --timezone</c> can
/// take a city or region name instead of a numeric code.
/// </summary>
internal static class TimezoneResolver
{
    /// <summary>
    /// Finds the single timezone matching <paramref name="query"/>. An exact
    /// (case-insensitive) name wins; otherwise a substring match must be unique.
    /// Throws <see cref="ArgumentException"/> on no match or ambiguity.
    /// </summary>
    public static TimezoneInfo Resolve(IReadOnlyList<TimezoneInfo> timezones, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Timezone query cannot be empty.");

        var trimmedQuery = query.Trim();

        var exactMatches = timezones
            .Where(timezone => string.Equals(timezone.Name, trimmedQuery, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (exactMatches.Count == 1)
            return exactMatches[0];

        var matches = timezones
            .Where(timezone => timezone.Name.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase)
                     || (timezone.StandardName?.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

        if (matches.Count == 1)
            return matches[0];

        if (matches.Count == 0)
            throw new ArgumentException(
                $"No timezone matches '{query}'. Run 'txc env timezone list --filter {query}' to browse, or pass the numeric code to --timezone.");

        var sample = string.Join(Environment.NewLine, matches.Take(10).Select(timezone => $"  {timezone.Code,-5} {timezone.Name}"));
        throw new ArgumentException(
            $"'{query}' matches {matches.Count} timezones. Narrow it down or pass the numeric code to --timezone:{Environment.NewLine}{sample}");
    }
}
