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
    /// Value must match a <see cref="ComponentDefinition.Name"/> from the registry.
    /// </summary>
    public const string ComponentTypeTagKey = "componentType";

    /// <summary>
    /// Resolves a user input to a template. Lookup order:
    /// <list type="number">
    ///   <item>Exact match on template short name or full name</item>
    ///   <item>Match via <c>componentType</c> tag using <see cref="ComponentDefinitionRegistry.GetByName"/></item>
    /// </list>
    /// When multiple templates share the same componentType tag (e.g. pp-entity and pp-entity-form
    /// both have componentType=Entity), returns the one with the shortest short name (the primary template).
    /// </summary>
    public static ITemplateInfo? Resolve(string input, IReadOnlyList<ITemplateInfo> templates)
    {
        // 1. Direct match on short name or full template name
        var direct = templates.FirstOrDefault(t =>
            t.ShortNameList.Any(sn => string.Equals(sn, input, StringComparison.OrdinalIgnoreCase))
            || string.Equals(t.Name, input, StringComparison.OrdinalIgnoreCase));

        if (direct != null)
            return direct;

        // 2. Resolve input via ComponentDefinitionRegistry, then match on componentType tag
        var def = ComponentDefinitionRegistry.GetByName(input);
        if (def == null)
            return null;

        return FindPrimaryTemplateForType(def.Name, templates);
    }

    /// <summary>
    /// Finds all templates tagged with the given component type name.
    /// </summary>
    public static IReadOnlyList<ITemplateInfo> FindAllForType(string componentTypeName, IReadOnlyList<ITemplateInfo> templates)
    {
        return templates
            .Where(t => string.Equals(GetComponentTypeName(t), componentTypeName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Finds the primary (shortest short name) template for a component type.
    /// Returns null if no template is tagged with this type.
    /// </summary>
    public static ITemplateInfo? FindPrimaryTemplateForType(string componentTypeName, IReadOnlyList<ITemplateInfo> templates)
    {
        return FindAllForType(componentTypeName, templates)
            .OrderBy(t => t.ShortNameList.FirstOrDefault()?.Length ?? int.MaxValue)
            .FirstOrDefault();
    }

    /// <summary>
    /// Extracts the componentType tag value from a template, if present.
    /// Tags are <see cref="ICacheTag"/> objects; the value is in <c>DefaultValue</c>.
    /// </summary>
    public static string? GetComponentTypeName(ITemplateInfo template)
    {
        return template.Tags.TryGetValue(ComponentTypeTagKey, out var ct) ? ct.DefaultValue : null;
    }
}
