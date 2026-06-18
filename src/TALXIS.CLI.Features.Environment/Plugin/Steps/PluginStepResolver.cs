using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Features.Environment.Plugin.Steps;

/// <summary>
/// Resolves a single plugin processing step from a user-supplied token that
/// may be either a step GUID or a step name. Name matching prefers an exact
/// (case-insensitive) hit and falls back to a unique substring match. A token
/// that matches nothing, or matches more than one step, yields an error
/// message instead of a step so callers can fail cleanly.
/// </summary>
public static class PluginStepResolver
{
    public sealed record Resolution(PluginStepRecord? Step, string? Error);

    public static Resolution Resolve(IReadOnlyList<PluginStepRecord> rows, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return new Resolution(null, "No step identifier supplied. Pass a step GUID or name.");

        token = token.Trim();

        if (Guid.TryParse(token, out var id))
        {
            var byId = rows.FirstOrDefault(r => r.Id == id);
            return byId is not null
                ? new Resolution(byId, null)
                : new Resolution(null, $"No plugin step found with id '{id}'.");
        }

        var exact = rows.Where(r => string.Equals(r.Name, token, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count == 1)
            return new Resolution(exact[0], null);
        if (exact.Count > 1)
            return new Resolution(null, AmbiguousError(token, exact));

        var partial = rows.Where(r => r.Name.Contains(token, StringComparison.OrdinalIgnoreCase)).ToList();
        if (partial.Count == 1)
            return new Resolution(partial[0], null);
        if (partial.Count > 1)
            return new Resolution(null, AmbiguousError(token, partial));

        return new Resolution(null, $"No plugin step found matching '{token}'.");
    }

    private static string AmbiguousError(string token, IReadOnlyList<PluginStepRecord> matches)
    {
        var names = string.Join("\n  ", matches.Take(10).Select(m => $"{m.Id}  {m.Name}"));
        return $"'{token}' is ambiguous; {matches.Count} steps match. Use the step id instead:\n  {names}";
    }
}
