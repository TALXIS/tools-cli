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
    ///   <item>Exact match on template short name or full template name</item>
    ///   <item>Exact match on <c>componentType</c> tag value (each template has a unique value)</item>
    ///   <item>Match via <see cref="ComponentDefinitionRegistry.GetByName"/> alias resolution, then tag match</item>
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

        // 2. Direct match on componentType tag value (e.g. "FormTab", "BpfStage", "Entity")
        var byTag = templates.FirstOrDefault(t =>
            string.Equals(GetComponentTypeName(t), input, StringComparison.OrdinalIgnoreCase));

        if (byTag != null)
            return byTag;

        // 3. Resolve via registry aliases (e.g. "Table" → "Entity", "Flow" → "Workflow")
        //    then match on componentType tag
        var def = ComponentDefinitionRegistry.GetByName(input);
        if (def != null)
        {
            var byAlias = templates.FirstOrDefault(t =>
                string.Equals(GetComponentTypeName(t), def.Name, StringComparison.OrdinalIgnoreCase));
            if (byAlias != null)
                return byAlias;
        }

        return null;
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
    /// Finds the template for a component type (1:1 mapping — returns exactly one or null).
    /// </summary>
    public static ITemplateInfo? FindTemplateForType(string componentTypeName, IReadOnlyList<ITemplateInfo> templates)
    {
        return templates.FirstOrDefault(t =>
            string.Equals(GetComponentTypeName(t), componentTypeName, StringComparison.OrdinalIgnoreCase));
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
