using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Connector.Operation;

[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List the operations (actions and triggers) of a connector in the profile's environment."
)]
public class ConnectorOperationListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectorOperationListCliCommand));

    [CliOption(Name = "--connector", Description = "Connector name as returned by 'connector list' (e.g. shared_commondataserviceforapps).", Required = true)]
    public string Connector { get; set; } = null!;

    [CliOption(Name = "--kind", Description = "Show only operations of this kind (action, trigger, webhook-trigger).", Required = false)]
    public string? Kind { get; set; }

    private static readonly string[] KnownKinds = ["action", "trigger", "webhook-trigger"];

    protected override async Task<int> ExecuteAsync()
    {
        if (!string.IsNullOrWhiteSpace(Kind) && !KnownKinds.Contains(Kind, StringComparer.OrdinalIgnoreCase))
        {
            Logger.LogError(
                "Invalid --kind '{Kind}'. Expected one of: action, trigger, webhook-trigger.", Kind);
            return ExitValidationError;
        }

        var service = TxcServices.Get<IConnectorCatalogService>();
        IReadOnlyList<ConnectorOperationSummary> operations = await service
            .ListOperationsAsync(Profile, Connector, CancellationToken.None)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(Kind))
        {
            operations = operations
                .Where(o => string.Equals(o.Kind, Kind, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        OutputFormatter.WriteList(operations, PrintTable);
        return ExitSuccess;
    }

    // Text-renderer callback invoked by OutputFormatter.WriteList, so OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintTable(IReadOnlyList<ConnectorOperationSummary> operations)
    {
        if (operations.Count == 0)
        {
            OutputWriter.WriteLine("No operations found.");
            return;
        }

        int idWidth = Math.Clamp(operations.Max(o => o.OperationId.Length), 20, 50);
        string header = $"{"Operation Id".PadRight(idWidth)} | {"Kind".PadRight(15)} | Summary";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var o in operations)
        {
            string id = o.OperationId.Length > idWidth ? o.OperationId[..(idWidth - 1)] + "." : o.OperationId;
            OutputWriter.WriteLine(
                $"{id.PadRight(idWidth)} | {o.Kind.PadRight(15)} | {o.Summary}");
        }
    }
#pragma warning restore TXC003
}
