using System.Text;
using System.Text.RegularExpressions;

namespace TALXIS.CLI.MCP;

/// <summary>
/// Pure helpers to pull the component template short-names a scaffolding recipe targets,
/// and render an authoritative parameter block so the model uses real parameter
/// names/types/choices/conditions instead of inventing them.
/// </summary>
public static class TemplateParameterEnricher
{
    // Matches template short-names like pp-entity, pp-entity-attribute, pp-api-endpoint.
    private static readonly Regex ShortNameToken = new(@"\bpp-[a-z0-9]+(?:-[a-z0-9]+)*\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The tool whose presence in a recipe means scaffolding is happening.
    /// </summary>
    public const string ScaffoldToolName = "workspace_component_create";

    /// <summary>
    /// If the recipe scaffolds via <see cref="ScaffoldToolName"/>, resolves the real
    /// parameters for each referenced template through <paramref name="provider"/> and
    /// appends an authoritative block. Best-effort: missing provider, no scaffolding,
    /// no templates, or failed lookups all leave the recipe unchanged.
    /// </summary>
    public static async Task<string?> EnrichAsync(
        string? recipe,
        IEnumerable<string> matchedToolNames,
        ITemplateParameterProvider? provider,
        CancellationToken ct)
    {
        if (provider is null || string.IsNullOrWhiteSpace(recipe))
            return recipe;

        var scaffolds = matchedToolNames.Any(n =>
            string.Equals(n, ScaffoldToolName, StringComparison.OrdinalIgnoreCase));
        if (!scaffolds)
            return recipe;

        var shortNames = ExtractTemplateShortNames(recipe);
        if (shortNames.Count == 0)
            return recipe;

        var resolved = new List<(string Template, IReadOnlyList<TemplateParameterInfo> Parameters)>();
        foreach (var shortName in shortNames)
        {
            var parameters = await provider.GetParametersAsync(shortName, ct);
            if (parameters is { Count: > 0 })
                resolved.Add((shortName, parameters));
        }

        if (resolved.Count == 0)
            return recipe;

        return recipe.TrimEnd() + "\n\n" + BuildAuthoritativeBlock(resolved);
    }

    /// <summary>
    /// Extracts the component template short-names referenced by a recipe. Prefers an
    /// explicit <c>TEMPLATES: a, b</c> line the model is asked to emit; otherwise falls
    /// back to scanning for <c>pp-*</c> tokens. Returns a de-duplicated, order-preserving list.
    /// </summary>
    public static IReadOnlyList<string> ExtractTemplateShortNames(string? recipe)
    {
        if (string.IsNullOrWhiteSpace(recipe))
            return Array.Empty<string>();

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string name)
        {
            var trimmed = name.Trim();
            if (trimmed.Length > 0 && seen.Add(trimmed))
                result.Add(trimmed);
        }

        // Primary: an explicit "TEMPLATES: pp-entity, pp-entity-attribute" line.
        foreach (var rawLine in recipe.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("TEMPLATES:", StringComparison.OrdinalIgnoreCase))
            {
                var list = line["TEMPLATES:".Length..];
                foreach (var part in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    // Keep only token-like values; ignore prose the model may append.
                    var m = ShortNameToken.Match(part);
                    Add(m.Success ? m.Value : part);
                }
            }
        }

        // Fallback: any pp-* token in the recipe body.
        if (result.Count == 0)
        {
            foreach (Match m in ShortNameToken.Matches(recipe))
                Add(m.Value);
        }

        return result;
    }

    /// <summary>
    /// Renders an authoritative markdown block listing the real parameters per template.
    /// </summary>
    public static string BuildAuthoritativeBlock(
        IReadOnlyList<(string Template, IReadOnlyList<TemplateParameterInfo> Parameters)> templates)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Exact template parameters (authoritative)");
        sb.AppendLine();
        sb.AppendLine("Use these real parameter names, types, and choices for `workspace_component_create` — do not invent or guess. Pass them as `Param` entries in `key=value` form.");

        foreach (var (template, parameters) in templates)
        {
            sb.AppendLine();
            sb.AppendLine($"### {template}");

            var required = parameters.Where(p => p.Required).ToList();
            var optional = parameters.Where(p => !p.Required).ToList();

            if (required.Count > 0)
            {
                sb.AppendLine("Required:");
                foreach (var p in required)
                    sb.AppendLine(FormatParam(p));
            }
            if (optional.Count > 0)
            {
                sb.AppendLine("Optional:");
                foreach (var p in optional)
                    sb.AppendLine(FormatParam(p));
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatParam(TemplateParameterInfo p)
    {
        var sb = new StringBuilder();
        sb.Append($"- `{p.Name}`");
        if (!string.IsNullOrEmpty(p.DataType))
            sb.Append($" ({p.DataType})");
        if (!string.IsNullOrEmpty(p.Choices))
            sb.Append($" — choices: {p.Choices}");
        if (!string.IsNullOrEmpty(p.DefaultValue))
            sb.Append($" [default: {p.DefaultValue}]");
        if (!string.IsNullOrEmpty(p.AppliesWhen))
            sb.Append($" — applies when: {p.AppliesWhen}");
        if (!string.IsNullOrEmpty(p.RequiredWhen))
            sb.Append($" — required when: {p.RequiredWhen}");
        if (!string.IsNullOrEmpty(p.Description))
            sb.Append($" — {p.Description}");
        return sb.ToString();
    }
}
