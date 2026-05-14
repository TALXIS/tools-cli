using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.OptionSet;

/// <summary>
/// Shows all values and labels for an option set — global or local.
/// Usage: <c>txc environment optionset describe --name &lt;schema-name&gt;</c>
/// Usage: <c>txc environment optionset describe --entity &lt;name&gt; --attribute &lt;name&gt;</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "get",
    Description = "Get values and labels for a global or local option set."
)]
#pragma warning disable TXC003
public class OptionSetShowCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(OptionSetShowCliCommand));

    [CliOption(Name = "--name", Description = "Schema name of a global option set.", Required = false)]
    public string? Name { get; set; }

    [CliOption(Name = "--entity", Description = "Entity logical name (for local option sets).", Required = false)]
    public string? Entity { get; set; }

    [CliOption(Name = "--attribute", Description = "Attribute logical name (for local option sets).", Required = false)]
    public string? Attribute { get; set; }

    [CliOption(Name = "--language", Description = "Language code (LCID) for labels, e.g. 1033 (English), 1029 (Czech). Defaults to the connection user's language.", Required = false)]
    public int? Language { get; set; }

    [CliOption(Name = "--label", Description = "Look up the integer value for a specific label text (case-insensitive). Outputs only the value for piping.", Required = false)]
    public string? LabelLookup { get; set; }

    [CliOption(Name = "--value", Description = "Look up the label text for a specific integer value. Outputs only the label for piping.", Required = false)]
    public int? ValueLookup { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        bool hasGlobal = !string.IsNullOrWhiteSpace(Name);
        bool hasLocal = !string.IsNullOrWhiteSpace(Entity) || !string.IsNullOrWhiteSpace(Attribute);

        if (hasGlobal && hasLocal)
        {
            Logger.LogError("Specify either --name (global) or --entity + --attribute (local), not both.");
            return ExitError;
        }
        if (!hasGlobal && !hasLocal)
        {
            Logger.LogError("Specify --name for a global option set, or --entity and --attribute for a local one.");
            return ExitError;
        }
        if (hasLocal && (string.IsNullOrWhiteSpace(Entity) || string.IsNullOrWhiteSpace(Attribute)))
        {
            Logger.LogError("Both --entity and --attribute are required for local option sets.");
            return ExitError;
        }

        // Collect options from global or local source
        IReadOnlyList<OptionValueRecord> options;

        if (hasGlobal)
        {
            var service = TxcServices.Get<IDataverseOptionSetService>();
            var detail = await service.DescribeGlobalOptionSetAsync(Profile, Name!, Language, CancellationToken.None).ConfigureAwait(false);
            options = detail.Options;

            if (LabelLookup is null && ValueLookup is null)
            {
                OutputFormatter.WriteData(detail, PrintGlobalDetail);
                return ExitSuccess;
            }
        }
        else
        {
            var metaService = TxcServices.Get<IDataverseEntityMetadataService>();
            var attrDetail = await metaService.GetAttributeDetailAsync(Profile, Entity!, Attribute!, CancellationToken.None).ConfigureAwait(false);

            // Extract options from attribute detail
            options = ExtractOptionsFromAttributeDetail(attrDetail);

            if (LabelLookup is null && ValueLookup is null)
            {
                OutputFormatter.WriteData(attrDetail, PrintAttributeDetail);
                return ExitSuccess;
            }
        }

        // Label → Value lookup
        if (LabelLookup is not null)
        {
            var match = options.FirstOrDefault(o =>
                string.Equals(o.Label, LabelLookup, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                Logger.LogError("No option found with label '{Label}'.", LabelLookup);
                return ExitError;
            }
            OutputWriter.WriteLine(match.Value.ToString());
            return ExitSuccess;
        }

        // Value → Label lookup
        if (ValueLookup is not null)
        {
            var match = options.FirstOrDefault(o => o.Value == ValueLookup.Value);
            if (match is null)
            {
                Logger.LogError("No option found with value {Value}.", ValueLookup.Value);
                return ExitError;
            }
            OutputWriter.WriteLine(match.Label ?? "");
            return ExitSuccess;
        }

        return ExitSuccess;
    }

    private static void PrintGlobalDetail(GlobalOptionSetDetailRecord detail)
    {
        OutputWriter.WriteLine($"Name:         {detail.Name}");
        OutputWriter.WriteLine($"Display Name: {detail.DisplayName ?? "-"}");
        OutputWriter.WriteLine($"Description:  {detail.Description ?? "-"}");
        OutputWriter.WriteLine($"Type:         {detail.OptionSetType}");
        OutputWriter.WriteLine($"Custom:       {detail.IsCustomOptionSet}");
        OutputWriter.WriteLine();
        PrintOptions(detail.Options);
    }

    private static void PrintAttributeDetail(Dictionary<string, object?> detail)
    {
        if (detail.TryGetValue("Logical Name", out var ln)) OutputWriter.WriteLine($"Attribute:    {ln}");
        if (detail.TryGetValue("Option Set Name", out var osn)) OutputWriter.WriteLine($"Option Set:   {osn}");
        if (detail.TryGetValue("Is Global Option Set", out var ig)) OutputWriter.WriteLine($"Global:       {ig}");
        OutputWriter.WriteLine();

        if (detail.TryGetValue("Options", out var opts) && opts is IEnumerable<object?> options)
        {
            var records = new List<OptionValueRecord>();
            foreach (var opt in options)
            {
                if (opt is Dictionary<string, object?> dict)
                {
                    var label = dict.GetValueOrDefault("Label")?.ToString();
                    var value = dict.GetValueOrDefault("Value") is int v ? v : 0;
                    records.Add(new OptionValueRecord(value, label, null));
                }
            }
            PrintOptions(records);
        }
        else
        {
            OutputWriter.WriteLine("  (not an option set attribute)");
        }
    }

    private static IReadOnlyList<OptionValueRecord> ExtractOptionsFromAttributeDetail(Dictionary<string, object?> detail)
    {
        if (detail.TryGetValue("Options", out var opts) && opts is IEnumerable<object?> options)
        {
            var records = new List<OptionValueRecord>();
            foreach (var opt in options)
            {
                if (opt is Dictionary<string, object?> dict)
                {
                    var label = dict.GetValueOrDefault("Label")?.ToString();
                    var value = dict.GetValueOrDefault("Value") is int v ? v : 0;
                    records.Add(new OptionValueRecord(value, label, null));
                }
            }
            return records;
        }
        return Array.Empty<OptionValueRecord>();
    }

    private static void PrintOptions(IReadOnlyList<OptionValueRecord> options)
    {
        OutputWriter.WriteLine($"Options ({options.Count}):");
        if (options.Count == 0)
        {
            OutputWriter.WriteLine("  (none)");
            return;
        }

        int valueWidth = Math.Max(5, options.Max(o => o.Value.ToString().Length));
        int labelWidth = Math.Clamp(options.Max(o => (o.Label ?? "").Length), 5, 60);

        OutputWriter.WriteLine($"  {"Value".PadRight(valueWidth)} | {"Label".PadRight(labelWidth)}");
        OutputWriter.WriteLine($"  {new string('-', valueWidth + labelWidth + 3)}");

        foreach (var opt in options)
        {
            OutputWriter.WriteLine($"  {opt.Value.ToString().PadRight(valueWidth)} | {(opt.Label ?? "-").PadRight(labelWidth)}");
        }
    }
}
