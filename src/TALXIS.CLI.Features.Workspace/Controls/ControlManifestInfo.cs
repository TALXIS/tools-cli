namespace TALXIS.CLI.Features.Workspace.Controls;

/// <summary>
/// Parsed PCF <c>ControlManifest.xml</c> — the runtime parameter schema of a custom control.
/// Replaces the per-control template approach: the manifest ships with the control itself,
/// so one generic attach command can validate and emit parameters for any control.
/// </summary>
public sealed class ControlManifestInfo
{
    /// <summary>Control namespace, e.g. <c>TALXIS.PCF</c>.</summary>
    public required string Namespace { get; init; }

    /// <summary>Control constructor, e.g. <c>Grid</c>.</summary>
    public required string Constructor { get; init; }

    /// <summary>Control version from the manifest.</summary>
    public string? Version { get; init; }

    /// <summary>Unprefixed qualified name, e.g. <c>TALXIS.PCF.Grid</c>.</summary>
    public string QualifiedName => $"{Namespace}.{Constructor}";

    /// <summary>
    /// Publisher-prefixed name used in FormXml (e.g. <c>talxis_TALXIS.PCF.Grid</c>).
    /// Resolved from the accompanying solution's <c>customizations.xml</c> when the manifest
    /// was read from a solution/package archive; null for a bare manifest file.
    /// </summary>
    public string? PrefixedName { get; set; }

    /// <summary>Dataset bindings declared by the control, in document order (e.g. <c>Grid</c>, <c>RibbonGroupingDataset</c>).</summary>
    public IReadOnlyList<string> DataSets { get; init; } = [];

    /// <summary>Input properties declared by the control.</summary>
    public IReadOnlyList<ControlManifestProperty> Properties { get; init; } = [];
}

/// <summary>A single <c>property</c> element from a control manifest.</summary>
public sealed class ControlManifestProperty
{
    public required string Name { get; init; }

    /// <summary>Dataverse type from <c>of-type</c> (e.g. <c>Enum</c>, <c>SingleLine.Text</c>, <c>Whole.None</c>, <c>Multiple</c>).</summary>
    public required string OfType { get; init; }

    public string? DefaultValue { get; init; }

    public bool Required { get; init; }

    /// <summary>Allowed values for <c>Enum</c> properties (the element texts of the <c>value</c> children).</summary>
    public IReadOnlyList<string> EnumValues { get; init; } = [];
}
