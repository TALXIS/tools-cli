using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Changeset;

/// <summary>
/// <c>txc environment changeset status</c> — shows all staged operations
/// in a formatted table.
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "status",
    Description = "Show all staged operations in the current changeset."
)]
#pragma warning disable TXC003
public class ChangesetStatusCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ChangesetStatusCliCommand));

    protected override Task<int> ExecuteAsync()
    {
        var store = TxcServices.Get<IChangesetStore>();
        var operations = store.GetAll();

        if (operations.Count == 0)
        {
            OutputWriter.WriteLine("Changeset is empty. Stage operations with --stage, then apply with 'txc environment changeset apply'.");
            return Task.FromResult(ExitSuccess);
        }

        // Column widths
        int idxWidth = 3;
        int catWidth = Math.Clamp(operations.Max(o => o.Category.Length), 8, 12);
        int opWidth = Math.Clamp(operations.Max(o => o.OperationType.Length), 9, 15);
        int targetWidth = Math.Clamp(operations.Max(o => o.TargetDescription.Length), 10, 40);
        int detailWidth = Math.Clamp(operations.Max(o => (o.Details ?? "").Length), 7, 40);

        string header = $"{"#".PadRight(idxWidth)} | {"Category".PadRight(catWidth)} | {"Operation".PadRight(opWidth)} | {"Target".PadRight(targetWidth)} | {"Details".PadRight(detailWidth)} | Staged At";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var op in operations)
        {
            string idx = op.Index.ToString().PadRight(idxWidth);
            string cat = Truncate(op.Category, catWidth).PadRight(catWidth);
            string opType = Truncate(op.OperationType, opWidth).PadRight(opWidth);
            string target = Truncate(op.TargetDescription, targetWidth).PadRight(targetWidth);
            string details = Truncate(op.Details ?? "", detailWidth).PadRight(detailWidth);
            string stagedAt = op.StagedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");

            OutputWriter.WriteLine($"{idx} | {cat} | {opType} | {target} | {details} | {stagedAt}");
        }

        OutputWriter.WriteLine();

        int schemaCount = operations.Count(o => o.Category == "schema");
        int dataCount = operations.Count(o => o.Category == "data");
        OutputWriter.WriteLine($"Total: {operations.Count} operations ({schemaCount} schema, {dataCount} data)");
        OutputWriter.WriteLine();
        OutputWriter.WriteLine("Apply strategies:");
        OutputWriter.WriteLine("  changeset apply --strategy batch [--continue-on-error]    Each data op in independent transaction");
        OutputWriter.WriteLine("  changeset apply --strategy transaction                     All data ops in single transaction (all-or-nothing)");
        OutputWriter.WriteLine("  changeset apply --strategy bulk                            High-throughput grouped by entity+operation");
        return Task.FromResult(ExitSuccess);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength) return value;
        return value[..(maxLength - 1)] + ".";
    }
}
