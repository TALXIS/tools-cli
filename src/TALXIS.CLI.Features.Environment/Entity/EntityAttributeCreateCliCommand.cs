using System.ComponentModel;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>
/// Creates an attribute (column) on a Dataverse entity.
/// Usage: <c>txc environment entity attribute create --entity &lt;name&gt; --name &lt;schema-name&gt; --type &lt;type&gt; [options]</c>
/// Use <c>txc environment entity attribute type describe &lt;type&gt;</c> to see type-specific parameters.
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "create",
    Description = "Create a column (attribute) on an entity. Use 'txc environment entity attribute type describe <type>' to see type-specific parameters."
)]
#pragma warning disable TXC003
public class EntityAttributeCreateCliCommand : StagedCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EntityAttributeCreateCliCommand));

    // === Required for all types ===

    [CliOption(Name = "--entity", Description = "Entity logical name.", Required = true)]
    public string Entity { get; set; } = null!;

    [CliOption(Name = "--name", Description = "Schema name for the new column.", Required = true)]
    public string Name { get; set; } = null!;

    [CliOption(Name = "--type", Description = "Column type: string, memo, number, decimal, float, money, bool, datetime, choice, multichoice, lookup, polymorphic-lookup, customer, image, file, bigint.", Required = true)]
    public AttributeTypeArg Type { get; set; }

    // === Optional for all types ===

    [CliOption(Name = "--display-name", Description = "The display name (label) for the column. Defaults to the schema name.", Required = false)]
    public string? DisplayName { get; set; }

    [CliOption(Name = "--description", Description = "Description for the column.", Required = false)]
    public string? Description { get; set; }

    [CliOption(Name = "--required", Description = "Requirement level: none, recommended, required.")]
    [DefaultValue("none")]
    public string RequiredLevel { get; set; } = "none";

    [CliOption(Name = "--solution", Description = "Solution unique name to add the column to.", Required = false)]
    public string? Solution { get; set; }

    [CliOption(Name = "--is-auditable", Description = "Enable auditing for this column to track value changes.", Required = false)]
    [DefaultValue(false)]
    public bool IsAuditable { get; set; }

    [CliOption(Name = "--is-searchable", Description = "Make this column available in Advanced Find and Dataverse search.", Required = false)]
    [DefaultValue(true)]
    public bool IsSearchable { get; set; } = true;

    [CliOption(Name = "--is-secured", Description = "Enable field-level security for this column.", Required = false)]
    [DefaultValue(false)]
    public bool IsSecured { get; set; }

    // === String/Memo ===

    [CliOption(Name = "--max-length", Description = "Maximum text length (string: 1-4000, memo: 1-1048576).")]
    public int? MaxLength { get; set; }

    [CliOption(Name = "--string-format", Description = "String format: text, email, url, phone, textarea, tickersymbol.", Required = false)]
    public string? StringFormat { get; set; }

    // === Number/Decimal/Float/Money ===

    [CliOption(Name = "--min-value", Description = "Minimum value.")]
    public double? MinValue { get; set; }

    [CliOption(Name = "--max-value", Description = "Maximum value.")]
    public double? MaxValue { get; set; }

    [CliOption(Name = "--precision", Description = "Decimal places (0-10 for decimal, 0-5 for float, 0-4 for money).")]
    public int? Precision { get; set; }

    [CliOption(Name = "--number-format", Description = "Integer format: none, duration, timezone, language, locale.", Required = false)]
    public string? NumberFormat { get; set; }

    [CliOption(Name = "--precision-source", Description = "Money precision source: attribute, organization, currency.", Required = false)]
    public string? PrecisionSource { get; set; }

    // === Bool ===

    [CliOption(Name = "--true-label", Description = "Label for the true option.")]
    [DefaultValue("Yes")]
    public string TrueLabel { get; set; } = "Yes";

    [CliOption(Name = "--false-label", Description = "Label for the false option.")]
    [DefaultValue("No")]
    public string FalseLabel { get; set; } = "No";

    // === DateTime ===

    [CliOption(Name = "--datetime-format", Description = "Date/time format: dateonly, dateandtime.", Required = false)]
    public string? DateTimeFormat { get; set; }

    [CliOption(Name = "--datetime-behavior", Description = "Date/time behavior: userlocaltime, dateonly, timezoneindependent.", Required = false)]
    public string? DateTimeBehavior { get; set; }

    // === Choice/Multichoice ===

    [CliOption(Name = "--options", Description = "Options as 'Label1,Label2' or 'Label1:100000000,Label2:100000001'.", Required = false)]
    public string? Options { get; set; }

    [CliOption(Name = "--global-optionset", Description = "Reference an existing global option set by name instead of --options.", Required = false)]
    public string? GlobalOptionSet { get; set; }

    // === Lookup ===

    [CliOption(Name = "--target-entity", Description = "Target entity for lookup columns.", Required = false)]
    public string? TargetEntity { get; set; }

    [CliOption(Name = "--target-entities", Description = "Comma-separated target entities for polymorphic lookup.", Required = false)]
    public string? TargetEntities { get; set; }

    [CliOption(Name = "--cascade-delete", Description = "Cascade delete behavior: cascade, removelink, restrict.")]
    [DefaultValue("removelink")]
    public string CascadeDelete { get; set; } = "removelink";

    // === Image/File ===

    [CliOption(Name = "--max-size-kb", Description = "Maximum file size in KB.")]
    public int? MaxSizeKb { get; set; }

    [CliOption(Name = "--can-store-full-image", Description = "Allow storing full-size images.")]
    [DefaultValue(true)]
    public bool CanStoreFullImage { get; set; } = true;

    protected override async Task<int> ExecuteAsync()
    {
        ValidateExecutionMode();

        if (Stage)
        {
            var options = BuildCreateOptions();

            var store = TxcServices.Get<IChangesetStore>();
            store.Add(new StagedOperation
            {
                Category = "schema",
                OperationType = "CREATE",
                TargetType = "attribute",
                TargetDescription = $"{Entity}.{Name}",
                Details = $"type: {Type}",
                Parameters = new Dictionary<string, object?>
                {
                    ["entity"] = options.EntityLogicalName,
                    ["name"] = options.SchemaName,
                    ["type"] = options.Type,
                    ["displayName"] = options.DisplayName,
                    ["description"] = options.Description,
                    ["requiredLevel"] = options.RequiredLevel,
                    ["solution"] = options.SolutionUniqueName,
                    ["maxLength"] = options.MaxLength,
                    ["stringFormat"] = options.StringFormat,
                    ["minValue"] = options.MinValue,
                    ["maxValue"] = options.MaxValue,
                    ["precision"] = options.Precision,
                    ["numberFormat"] = options.NumberFormat,
                    ["precisionSource"] = options.PrecisionSource,
                    ["trueLabel"] = options.TrueLabel,
                    ["falseLabel"] = options.FalseLabel,
                    ["dateTimeFormat"] = options.DateTimeFormat,
                    ["dateTimeBehavior"] = options.DateTimeBehavior,
                    ["options"] = Options,
                    ["globalOptionSetName"] = options.GlobalOptionSetName,
                    ["targetEntity"] = options.TargetEntity,
                    ["targetEntities"] = options.TargetEntities,
                    ["cascadeDelete"] = options.CascadeDelete,
                    ["maxSizeKb"] = options.MaxSizeKb,
                    ["canStoreFullImage"] = options.CanStoreFullImage,
                    ["isAuditable"] = options.IsAuditable,
                    ["isSearchable"] = options.IsSearchable,
                    ["isSecured"] = options.IsSecured
                }
            });
            OutputWriter.WriteLine($"Staged: CREATE attribute '{Entity}.{Name}' (type: {Type})");
            return ExitSuccess;
        }

        try
        {
            ValidateTypeSpecificParams();

            var options = BuildCreateOptions();
            var service = TxcServices.Get<IDataverseEntityMetadataService>();
            await service.CreateAttributeAsync(Profile, options, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            Logger.LogError("{Error}", ex.Message);
            return ExitError;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "environment entity attribute create failed");
            return ExitError;
        }

        OutputWriter.WriteLine($"Attribute '{Name}' ({Type}) created on entity '{Entity}'.");
        return ExitSuccess;
    }

    /// <summary>Validates that type-specific required parameters are provided.</summary>
    private void ValidateTypeSpecificParams()
    {
        switch (Type)
        {
            case AttributeTypeArg.Lookup:
                if (string.IsNullOrWhiteSpace(TargetEntity))
                    throw new ArgumentException("--target-entity is required for lookup type.");
                break;

            case AttributeTypeArg.PolymorphicLookup:
                if (string.IsNullOrWhiteSpace(TargetEntities))
                    throw new ArgumentException("--target-entities is required for polymorphic-lookup type.");
                break;

            case AttributeTypeArg.Choice:
            case AttributeTypeArg.Multichoice:
                if (string.IsNullOrWhiteSpace(Options) && string.IsNullOrWhiteSpace(GlobalOptionSet))
                    throw new ArgumentException("--options or --global-optionset is required for choice/multichoice type.");
                break;
        }
    }

    /// <summary>Maps CLI properties into a strongly-typed <see cref="CreateAttributeOptions"/> request object.</summary>
    private CreateAttributeOptions BuildCreateOptions()
    {
        // Validate enum-like string params early so the service layer gets clean input.
        ValidateRequiredLevel(RequiredLevel);
        if (StringFormat is not null) ValidateStringFormat(StringFormat);
        if (NumberFormat is not null) ValidateNumberFormat(NumberFormat);
        if (PrecisionSource is not null) ValidatePrecisionSource(PrecisionSource);
        if (DateTimeFormat is not null) ValidateDateTimeFormat(DateTimeFormat);
        if (DateTimeBehavior is not null) ValidateDateTimeBehavior(DateTimeBehavior);
        ValidateCascadeDelete(CascadeDelete);

        return new CreateAttributeOptions
        {
            EntityLogicalName = Entity,
            SchemaName = Name,
            Type = Type.ToString().ToLowerInvariant(),
            DisplayName = DisplayName ?? Name,
            Description = Description,
            RequiredLevel = RequiredLevel.ToLowerInvariant(),
            SolutionUniqueName = Solution,

            // String/Memo
            MaxLength = MaxLength,
            StringFormat = StringFormat?.ToLowerInvariant(),

            // Numeric
            MinValue = MinValue,
            MaxValue = MaxValue,
            Precision = Precision,
            NumberFormat = NumberFormat?.ToLowerInvariant(),
            PrecisionSource = ParsePrecisionSource(PrecisionSource),

            // Bool
            TrueLabel = TrueLabel,
            FalseLabel = FalseLabel,

            // DateTime
            DateTimeFormat = DateTimeFormat?.ToLowerInvariant(),
            DateTimeBehavior = DateTimeBehavior?.ToLowerInvariant(),

            // Choice
            Options = ParseOptionTuples(Options),
            GlobalOptionSetName = GlobalOptionSet,

            // Lookup
            TargetEntity = TargetEntity,
            TargetEntities = TargetEntities?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            CascadeDelete = CascadeDelete.ToLowerInvariant(),

            // Image/File
            MaxSizeKb = MaxSizeKb,
            CanStoreFullImage = CanStoreFullImage,

            // Shared metadata properties
            IsAuditable = IsAuditable,
            IsSearchable = IsSearchable,
            IsSecured = IsSecured
        };
    }

    // --- Validation helpers ---

    private static void ValidateRequiredLevel(string value)
    {
        if (value.ToLowerInvariant() is not ("none" or "recommended" or "required"))
            throw new ArgumentException($"Invalid --required value '{value}'. Expected: none, recommended, required.");
    }

    private static void ValidateStringFormat(string value)
    {
        if (value.ToLowerInvariant() is not ("text" or "email" or "url" or "phone" or "textarea" or "tickersymbol"))
            throw new ArgumentException($"Invalid --string-format value '{value}'. Expected: text, email, url, phone, textarea, tickersymbol.");
    }

    private static void ValidateNumberFormat(string value)
    {
        if (value.ToLowerInvariant() is not ("none" or "duration" or "timezone" or "language" or "locale"))
            throw new ArgumentException($"Invalid --number-format value '{value}'. Expected: none, duration, timezone, language, locale.");
    }

    private static void ValidatePrecisionSource(string value)
    {
        if (value.ToLowerInvariant() is not ("attribute" or "organization" or "currency"))
            throw new ArgumentException($"Invalid --precision-source value '{value}'. Expected: attribute, organization, currency.");
    }

    private static void ValidateDateTimeFormat(string value)
    {
        if (value.ToLowerInvariant() is not ("dateonly" or "dateandtime"))
            throw new ArgumentException($"Invalid --datetime-format value '{value}'. Expected: dateonly, dateandtime.");
    }

    private static void ValidateDateTimeBehavior(string value)
    {
        if (value.ToLowerInvariant() is not ("userlocaltime" or "dateonly" or "timezoneindependent"))
            throw new ArgumentException($"Invalid --datetime-behavior value '{value}'. Expected: userlocaltime, dateonly, timezoneindependent.");
    }

    private static void ValidateCascadeDelete(string value)
    {
        if (value.ToLowerInvariant() is not ("cascade" or "removelink" or "restrict"))
            throw new ArgumentException($"Invalid --cascade-delete value '{value}'. Expected: cascade, removelink, restrict.");
    }

    private static int? ParsePrecisionSource(string? value) => value?.ToLowerInvariant() switch
    {
        null => null,
        "attribute" => 0,
        "organization" => 1,
        "currency" => 2,
        _ => null // already validated above
    };

    /// <summary>
    /// Parses option labels in the format 'Label1,Label2' or 'Label1:100000000,Label2:100000001'.
    /// </summary>
    private static (string Label, int Value)[]? ParseOptionTuples(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var items = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new (string Label, int Value)[items.Length];
        int autoValue = 100_000_000;

        for (int i = 0; i < items.Length; i++)
        {
            var parts = items[i].Split(':', 2, StringSplitOptions.TrimEntries);
            string label = parts[0];
            int optionValue = parts.Length == 2 && int.TryParse(parts[1], out var parsed) ? parsed : autoValue++;
            if (parts.Length < 2) autoValue = optionValue + 1; // only increment if auto-assigning
            result[i] = (label, optionValue);
        }

        return result;
    }
}

/// <summary>
/// Supported attribute types for the <c>txc environment entity attribute create</c> command.
/// DotMake uses <see cref="DescriptionAttribute"/> for kebab-case CLI names.
/// </summary>
public enum AttributeTypeArg
{
    String,
    Memo,
    Number,
    Decimal,
    Float,
    Money,
    Bool,
    DateTime,
    Choice,
    Multichoice,
    Lookup,
    [Description("polymorphic-lookup")] PolymorphicLookup,
    Customer,
    Image,
    File,
    BigInt
}
