using Microsoft.TemplateEngine.Abstractions;
using TALXIS.Platform.Metadata;

namespace TALXIS.CLI.Features.Workspace.TemplateEngine;

/// <summary>
/// Resolves a user-supplied component identifier to a matching template.
/// Accepts template short names (pp-entity), full template names, registry names (Entity),
/// aliases (Table), and integer type codes (1). Uses the <c>componentType</c> tag in
/// template.json to map between registry names and templates.
/// </summary>
public static class TemplateResolver
{
    /// <summary>
    /// The tag key used in template.json to declare the component type name.
    /// Value may be either the canonical <see cref="ComponentDefinition.Name"/> from the
    /// registry (e.g. "Role") or any of its registered aliases (e.g. "SecurityRole") — both
    /// forms are normalized through <see cref="ComponentDefinitionRegistry.GetByName"/> before
    /// comparison, so either resolves correctly.
    /// </summary>
    public const string ComponentTypeTagKey = "componentType";

    /// <summary>
    /// Resolves a user input to a template. Lookup order:
    /// <list type="number">
    ///   <item>Exact match on template short name or full template name</item>
    ///   <item>Match on <c>componentType</c> tag value, with both the input and the tag
    ///   normalized through <see cref="ComponentDefinitionRegistry.GetByName"/> so that a
    ///   canonical registry name (e.g. "Role") and any of its aliases (e.g. "SecurityRole")
    ///   resolve to the same template regardless of which form the template happens to be
    ///   tagged with</item>
    /// </list>
    /// </summary>
    public static ITemplateInfo? Resolve(string input, IReadOnlyList<ITemplateInfo> templates)
    {
        // 1. Direct match on short name or full template name (e.g. "pp-entity", "pp-form-tab")
        var direct = templates.FirstOrDefault(t =>
            t.ShortNameList.Any(sn => string.Equals(sn, input, StringComparison.OrdinalIgnoreCase))
            || string.Equals(t.Name, input, StringComparison.OrdinalIgnoreCase));

        if (direct != null)
            return direct;

        // 2. Match on componentType tag value, normalizing both sides through the registry
        //    (e.g. "Role" and "SecurityRole" both normalize to the same canonical name, so
        //    either input matches a template tagged with either form)
        var canonicalInput = Canonicalize(input);
        return templates.FirstOrDefault(t =>
            string.Equals(GetCanonicalComponentTypeName(t), canonicalInput, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Finds all templates tagged with the given component type name. Both the input and each
    /// template's tag are normalized through <see cref="ComponentDefinitionRegistry.GetByName"/>
    /// so canonical names and aliases match symmetrically.
    /// </summary>
    public static IReadOnlyList<ITemplateInfo> FindAllForType(string componentTypeName, IReadOnlyList<ITemplateInfo> templates)
    {
        var canonicalType = Canonicalize(componentTypeName);
        return templates
            .Where(t => string.Equals(GetCanonicalComponentTypeName(t), canonicalType, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Finds the template for a component type (1:1 mapping — returns exactly one or null).
    /// Both the input and each template's tag are normalized through
    /// <see cref="ComponentDefinitionRegistry.GetByName"/> so canonical names and aliases match
    /// symmetrically.
    /// </summary>
    public static ITemplateInfo? FindTemplateForType(string componentTypeName, IReadOnlyList<ITemplateInfo> templates)
    {
        var canonicalType = Canonicalize(componentTypeName);
        return templates.FirstOrDefault(t =>
            string.Equals(GetCanonicalComponentTypeName(t), canonicalType, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Extracts the componentType tag value from a template, if present.
    /// Tags are <see cref="ICacheTag"/> objects; the value is in <c>DefaultValue</c>.
    /// </summary>
    public static string? GetComponentTypeName(ITemplateInfo template)
    {
        return template.Tags.TryGetValue(ComponentTypeTagKey, out var ct) ? ct.DefaultValue : null;
    }

    /// <summary>
    /// Extracts the componentType tag value from a template and normalizes it through
    /// <see cref="ComponentDefinitionRegistry.GetByName"/> to its canonical registry name.
    /// If the tag value has no registry entry, it is returned unchanged (normalization is a
    /// no-op for tags like "FormTab" that aren't in the registry).
    /// </summary>
    public static string? GetCanonicalComponentTypeName(ITemplateInfo template)
    {
        var raw = GetComponentTypeName(template);
        if (raw is null)
            return null;

        return Canonicalize(raw);
    }

    /// <summary>
    /// Normalizes a component type name or alias to its canonical <see cref="ComponentDefinition.Name"/>
    /// via <see cref="ComponentDefinitionRegistry.GetByName"/>. If the value has no registry entry, it is
    /// returned unchanged (normalization is a no-op for values like "FormTab" that aren't in the registry).
    /// This is the single normalization rule shared by <see cref="Resolve"/>, <see cref="FindAllForType"/>,
    /// <see cref="FindTemplateForType"/>, and <see cref="GetCanonicalComponentTypeName"/> so alias
    /// resolution cannot drift between them.
    /// </summary>
    private static string Canonicalize(string value) =>
        ComponentDefinitionRegistry.GetByName(value)?.Name ?? value;
}
