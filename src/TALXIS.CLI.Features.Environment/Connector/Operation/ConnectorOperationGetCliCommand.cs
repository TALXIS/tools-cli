using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Connector.Operation;

[CliReadOnly]
[CliCommand(
    Name = "get",
    Description = "Gets the exact inputs.parameters keys, types, allowed values, dynamic resolvers and required action type for one connector operation from the LIVE connected environment. Requires an active profile. Call this before writing a connector action instead of recalling parameter names."
)]
public class ConnectorOperationGetCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectorOperationGetCliCommand));

    [CliArgument(Description = "Connector name, for example shared_commondataserviceforapps.")]
    public string Connector { get; set; } = string.Empty;

    [CliArgument(Description = "Operation ID, for example CreateRecord.")]
    public string Operation { get; set; } = string.Empty;

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IConnectorMetadataService>();
        var operation = await service.GetOperationAsync(Profile, Connector, Operation, CancellationToken.None)
            .ConfigureAwait(false);

        OutputFormatter.WriteData(operation, PrintOperation);
        return ExitSuccess;
    }

#pragma warning disable TXC003
    private static void PrintOperation(OperationDetail operation)
    {
        OutputWriter.WriteLine($"{operation.OperationId} ({operation.ApiId})");
        if (!string.IsNullOrWhiteSpace(operation.Summary))
            OutputWriter.WriteLine(operation.Summary);
        OutputWriter.WriteLine($"Action type: {operation.ActionType}");
        if (operation.IsDeprecated)
            OutputWriter.WriteLine("DEPRECATED: use the current replacement operation.");
        OutputWriter.WriteLine(string.Empty);

        if (operation.Parameters.Count == 0) { OutputWriter.WriteLine("This operation takes no parameters."); return; }

        var nameWidth = Math.Max(operation.Parameters.Max(p => p.Name.Length), 20);
        var indent = new string(' ', nameWidth);
        var header = $"{"Parameter".PadRight(nameWidth)} | {"Type".PadRight(8)} | Req | Description";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var p in operation.Parameters)
        {
            OutputWriter.WriteLine($"{p.Name.PadRight(nameWidth)} | {(p.Type ?? "-").PadRight(8)} | {(p.Required ? "yes" : "no ")} | {p.Description}");

            if (p.AllowedValues is { Count: > 0 } allowed)
                OutputWriter.WriteLine($"{indent} | allowed: {string.Join(", ", allowed)}");

            // Without these the parameter looks free-form when its values, or
            // its whole sub-schema, are resolved by another call.
            Describe("values from", p.DynamicValues);
            Describe("tree from", p.DynamicTree);
            Describe("sub-schema from", p.DynamicSchema);
        }

        void Describe(string label, DynamicValuesRef? reference)
        {
            if (reference is null)
                return;

            var arguments = reference.Parameters is { Count: > 0 } bag
                ? " (" + string.Join(", ", bag.Select(kv => $"{kv.Key}={kv.Value}")) + ")"
                : string.Empty;
            OutputWriter.WriteLine($"{indent} | {label}: {reference.OperationId}{arguments}");
        }
    }
#pragma warning restore TXC003
}
