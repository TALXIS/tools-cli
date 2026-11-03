using System.Text.Json.Serialization;

namespace TALXIS.CLI.MCP;

/// <summary>
/// A single scaffolding parameter for a component template, mirroring the JSON shape
/// emitted by `txc workspace component parameter list &lt;type&gt; --format json`.
/// Used by the guide tools to attach authoritative parameter details to scaffolding
/// recipes instead of letting the model invent names.
/// </summary>
public sealed class TemplateParameterInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("dataType")]
    public string? DataType { get; init; }

    [JsonPropertyName("defaultValue")]
    public string? DefaultValue { get; init; }

    [JsonPropertyName("required")]
    public bool Required { get; init; }

    [JsonPropertyName("choices")]
    public string? Choices { get; init; }

    /// <summary>Condition under which the parameter applies (e.g. <c>AttributeType == "Text"</c>); null if unconditional.</summary>
    [JsonPropertyName("appliesWhen")]
    public string? AppliesWhen { get; init; }

    /// <summary>Condition under which the parameter is required; null if unconditional.</summary>
    [JsonPropertyName("requiredWhen")]
    public string? RequiredWhen { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

/// <summary>
/// Supplies the real parameter list for a component template. Implemented over a
/// subprocess call to the CLI in production; faked in tests.
/// </summary>
public interface ITemplateParameterProvider
{
    /// <summary>
    /// Returns the parameters for a template short-name (e.g. <c>pp-entity-attribute</c>),
    /// or null if the template is unknown or the lookup failed.
    /// </summary>
    Task<IReadOnlyList<TemplateParameterInfo>?> GetParametersAsync(string templateShortName, CancellationToken ct);
}
