namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Strongly-typed request object for creating an attribute (column) on a Dataverse entity.
/// Uses only primitive types so the Core project does not depend on the Xrm SDK.
/// The service layer maps these values to the corresponding SDK metadata types.
/// </summary>
public sealed record CreateAttributeOptions
{
    // === Shared / required ===

    public required string EntityLogicalName { get; init; }
    public required string SchemaName { get; init; }

    /// <summary>Lowercase type key: string, memo, number, decimal, float, money, bool, datetime, choice, multichoice, lookup, polymorphiclookup, customer, image, file, bigint.</summary>
    public required string Type { get; init; }

    public string? DisplayName { get; init; }
    public string? Description { get; init; }

    /// <summary>Requirement level string: "none", "recommended", or "required".</summary>
    public string RequiredLevel { get; init; } = "none";

    public string? SolutionUniqueName { get; init; }

    // === String/Memo ===

    public int? MaxLength { get; init; }

    /// <summary>String format name: text, email, url, phone, textarea, tickersymbol.</summary>
    public string? StringFormat { get; init; }

    // === Numeric (Number/Decimal/Float/Money) ===

    public double? MinValue { get; init; }
    public double? MaxValue { get; init; }
    public int? Precision { get; init; }

    /// <summary>Integer format: none, duration, timezone, language, locale.</summary>
    public string? NumberFormat { get; init; }

    /// <summary>Money precision source: 0 = attribute, 1 = organization, 2 = currency.</summary>
    public int? PrecisionSource { get; init; }

    // === Bool ===

    public string TrueLabel { get; init; } = "Yes";
    public string FalseLabel { get; init; } = "No";

    // === DateTime ===

    /// <summary>Date/time format: dateonly, dateandtime.</summary>
    public string? DateTimeFormat { get; init; }

    /// <summary>Date/time behavior: userlocaltime, dateonly, timezoneindependent.</summary>
    public string? DateTimeBehavior { get; init; }

    // === Choice/Multichoice ===

    /// <summary>Option labels and values as tuples (Label, Value). Null when using a global option set.</summary>
    public (string Label, int Value)[]? Options { get; init; }

    public string? GlobalOptionSetName { get; init; }

    // === Lookup ===

    public string? TargetEntity { get; init; }

    /// <summary>Target entities for polymorphic lookup (one relationship per entity).</summary>
    public string[]? TargetEntities { get; init; }

    /// <summary>Cascade delete behavior: cascade, removelink, restrict.</summary>
    public string CascadeDelete { get; init; } = "removelink";

    // === Image/File ===

    public int? MaxSizeKb { get; init; }
    public bool CanStoreFullImage { get; init; } = true;

    // === Shared metadata properties ===

    public bool IsAuditable { get; init; }
    public bool IsSearchable { get; init; } = true;
    public bool IsSecured { get; init; }
}
