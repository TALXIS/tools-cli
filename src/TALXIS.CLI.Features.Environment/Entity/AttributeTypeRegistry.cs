using System.Collections.ObjectModel;

namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>
/// Hardcoded registry of all supported Dataverse attribute types and their parameters.
/// No Dataverse connection needed — this is a static reference for CLI introspection.
/// </summary>
public static class AttributeTypeRegistry
{
    /// <summary>All supported attribute types.</summary>
    public static IReadOnlyList<AttributeTypeInfo> AllTypes { get; } = BuildAllTypes();

    /// <summary>Looks up a type by its CLI name (case-insensitive).</summary>
    public static AttributeTypeInfo? Get(string typeName) =>
        AllTypes.FirstOrDefault(t => t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Shared parameters that apply to all (or most) attribute types.
    /// These are not repeated inside each type definition.
    /// </summary>
    public static IReadOnlyList<string> SharedParameterNames { get; } = new[]
    {
        "entity", "name", "display-name", "description", "required", "solution"
    };

    private static ReadOnlyCollection<AttributeTypeInfo> BuildAllTypes()
    {
        var types = new List<AttributeTypeInfo>
        {
            new("string", "Single-line text", "StringAttributeMetadata", new AttributeParameterInfo[]
            {
                new("max-length", "int", "Maximum length of the text value.", "StringAttributeMetadata.MaxLength",
                    Default: "200", Min: 1, Max: 4000),
                new("string-format", "enum", "The format of the string field.",
                    "StringAttributeMetadata.FormatName",
                    Default: "text",
                    EnumValues: new[] { "text", "email", "url", "phone", "textarea", "tickersymbol" }),
            }),

            new("memo", "Multi-line text", "MemoAttributeMetadata", new AttributeParameterInfo[]
            {
                new("max-length", "int", "Maximum length of the memo value.", "MemoAttributeMetadata.MaxLength",
                    Default: "2000", Min: 1, Max: 1048576),
            }),

            new("number", "Whole number", "IntegerAttributeMetadata", new AttributeParameterInfo[]
            {
                new("min-value", "int", "Minimum allowed value.", "IntegerAttributeMetadata.MinValue",
                    Default: "-2147483648"),
                new("max-value", "int", "Maximum allowed value.", "IntegerAttributeMetadata.MaxValue",
                    Default: "2147483647"),
                new("number-format", "enum", "Display format for the integer.",
                    "IntegerAttributeMetadata.Format",
                    Default: "none",
                    EnumValues: new[] { "none", "duration", "timezone", "language", "locale" }),
            }),

            new("decimal", "Decimal number", "DecimalAttributeMetadata", new AttributeParameterInfo[]
            {
                new("min-value", "decimal", "Minimum allowed value.", "DecimalAttributeMetadata.MinValue"),
                new("max-value", "decimal", "Maximum allowed value.", "DecimalAttributeMetadata.MaxValue"),
                new("precision", "int", "Number of decimal places.", "DecimalAttributeMetadata.Precision",
                    Default: "2", Min: 0, Max: 10),
            }),

            new("float", "Float number", "DoubleAttributeMetadata", new AttributeParameterInfo[]
            {
                new("min-value", "double", "Minimum allowed value.", "DoubleAttributeMetadata.MinValue"),
                new("max-value", "double", "Maximum allowed value.", "DoubleAttributeMetadata.MaxValue"),
                new("precision", "int", "Number of decimal places.", "DoubleAttributeMetadata.Precision",
                    Default: "2", Min: 0, Max: 5),
            }),

            new("money", "Currency", "MoneyAttributeMetadata", new AttributeParameterInfo[]
            {
                new("min-value", "decimal", "Minimum allowed value.", "MoneyAttributeMetadata.MinValue"),
                new("max-value", "decimal", "Maximum allowed value.", "MoneyAttributeMetadata.MaxValue"),
                new("precision", "int", "Number of decimal places.", "MoneyAttributeMetadata.Precision",
                    Default: "2", Min: 0, Max: 4),
                new("precision-source", "enum",
                    "Where precision is sourced from: the attribute itself, the organization, or the currency.",
                    "MoneyAttributeMetadata.PrecisionSource",
                    Default: "attribute",
                    EnumValues: new[] { "attribute", "organization", "currency" }),
            }),

            new("bool", "Yes/No", "BooleanAttributeMetadata", new AttributeParameterInfo[]
            {
                new("true-label", "string", "Label shown for the true/yes value.",
                    "BooleanAttributeMetadata.OptionSet.TrueOption.Label", Default: "Yes"),
                new("false-label", "string", "Label shown for the false/no value.",
                    "BooleanAttributeMetadata.OptionSet.FalseOption.Label", Default: "No"),
            }),

            new("datetime", "Date and time", "DateTimeAttributeMetadata", new AttributeParameterInfo[]
            {
                new("datetime-format", "enum",
                    "Whether to show date only or date and time.",
                    "DateTimeAttributeMetadata.Format",
                    Default: "dateandtime",
                    EnumValues: new[] { "dateonly", "dateandtime" }),
                new("datetime-behavior", "enum",
                    "How the date/time value is stored and displayed. 'userlocaltime' adjusts to user's timezone, 'dateonly' stores date without time, 'timezoneindependent' stores as-is.",
                    "DateTimeAttributeMetadata.DateTimeBehavior",
                    Default: "userlocaltime",
                    EnumValues: new[] { "userlocaltime", "dateonly", "timezoneindependent" }),
            }),

            new("choice", "Single-select option set", "PicklistAttributeMetadata", new AttributeParameterInfo[]
            {
                new("options", "string",
                    "Comma-separated option labels (required unless global-optionset is specified).",
                    "PicklistAttributeMetadata.OptionSet.Options"),
                new("global-optionset", "string",
                    "Logical name of an existing global option set to reuse.",
                    "PicklistAttributeMetadata.OptionSet"),
            }),

            new("multichoice", "Multi-select option set", "MultiSelectPicklistAttributeMetadata",
                new AttributeParameterInfo[]
                {
                    new("options", "string",
                        "Comma-separated option labels (required unless global-optionset is specified).",
                        "MultiSelectPicklistAttributeMetadata.OptionSet.Options"),
                    new("global-optionset", "string",
                        "Logical name of an existing global option set to reuse.",
                        "MultiSelectPicklistAttributeMetadata.OptionSet"),
                }),

            new("lookup", "Single-entity lookup (1:N)", "LookupAttributeMetadata", new AttributeParameterInfo[]
            {
                new("target-entity", "string", "The logical name of the target entity (required).",
                    "LookupAttributeMetadata.Targets"),
                new("cascade-delete", "enum",
                    "Behavior when the referenced record is deleted.",
                    "OneToManyRelationshipMetadata.CascadeConfiguration.Delete",
                    Default: "removelink",
                    EnumValues: new[] { "cascade", "removelink", "restrict" }),
            }),

            new("polymorphic-lookup", "Multi-entity lookup (polymorphic)", "LookupAttributeMetadata",
                new AttributeParameterInfo[]
                {
                    new("target-entities", "string",
                        "Comma-separated logical names of target entities (required).",
                        "LookupAttributeMetadata.Targets"),
                    new("cascade-delete", "enum",
                        "Behavior when the referenced record is deleted.",
                        "OneToManyRelationshipMetadata.CascadeConfiguration.Delete",
                        Default: "removelink",
                        EnumValues: new[] { "cascade", "removelink", "restrict" }),
                }),

            new("customer", "Customer lookup (Account + Contact)", "LookupAttributeMetadata",
                Array.Empty<AttributeParameterInfo>()),

            new("image", "Image", "ImageAttributeMetadata", new AttributeParameterInfo[]
            {
                new("max-size-kb", "int", "Maximum image size in kilobytes.",
                    "ImageAttributeMetadata.MaxSizeInKB", Default: "10240"),
                new("can-store-full-image", "bool", "Whether the full-size image is stored in addition to the thumbnail.",
                    "ImageAttributeMetadata.CanStoreFullImage", Default: "true"),
            }),

            new("file", "File attachment", "FileAttributeMetadata", new AttributeParameterInfo[]
            {
                new("max-size-kb", "int", "Maximum file size in kilobytes.",
                    "FileAttributeMetadata.MaxSizeInKB", Default: "131072"),
            }),
        };

        return types.AsReadOnly();
    }
}

/// <summary>Describes a supported attribute type and its type-specific parameters.</summary>
public record AttributeTypeInfo(
    string Name,
    string Description,
    string SdkType,
    IReadOnlyList<AttributeParameterInfo> Parameters);

/// <summary>Describes a single parameter for an attribute type.</summary>
public record AttributeParameterInfo(
    string Name,
    string Type,
    string Description,
    string SdkProperty,
    string? Default = null,
    string[]? EnumValues = null,
    double? Min = null,
    double? Max = null);
