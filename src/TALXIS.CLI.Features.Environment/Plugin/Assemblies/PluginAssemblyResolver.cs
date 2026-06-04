using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Features.Environment.Plugin.Assemblies;

/// <summary>
/// Resolves a single plugin assembly from a token that may be an assembly GUID
/// or a name (exact, then unique substring). Mirrors the step resolver so the
/// CLI behaves consistently when a user passes a name or id.
/// </summary>
public static class PluginAssemblyResolver
{
    public sealed record Resolution(PluginAssemblyRecord? Assembly, string? Error);

    public static Resolution Resolve(IReadOnlyList<PluginAssemblyRecord> rows, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return new Resolution(null, "No assembly identifier supplied. Pass an assembly GUID or name.");

        token = token.Trim();

        if (Guid.TryParse(token, out var id))
        {
            var byId = rows.FirstOrDefault(r => r.Id == id);
            return byId is not null
                ? new Resolution(byId, null)
                : new Resolution(null, $"No plugin assembly found with id '{id}'.");
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

        return new Resolution(null, $"No plugin assembly found matching '{token}'.");
    }

    private static string AmbiguousError(string token, IReadOnlyList<PluginAssemblyRecord> matches)
    {
        var names = string.Join("\n  ", matches.Take(10).Select(m => $"{m.Id}  {m.Name}"));
        return $"'{token}' is ambiguous; {matches.Count} assemblies match. Use the assembly id or a longer name:\n  {names}";
    }
}
