using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Connector.Operation;

/// <summary>
/// <c>txc environment connector operation get</c> — the authoritative
/// parameter specification for one operation.
/// </summary>
[CliReadOnly]
[CliWorkflow("environment-inspection")]
[CliCommand(
    Name = "get",
    Description = "Gets the exact parameter names, types, allowed values and required action type for one connector operation from the LIVE connected environment. Requires an active profile. Call this before writing a connector action instead of recalling parameter names."
)]
public class ConnectorOperationGetCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectorOperationGetCliCommand));

    [CliArgument(Description = "Connector name, for example shared_teams.")]
    public string Connector { get; set; } = string.Empty;

    [CliArgument(Description = "Operation ID, for example PostMessageToConversation.")]
    public string Operation { get; set; } = string.Empty;

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IPowerAutomateConnectorService>();
        var operation = await service.GetOperationDetailsAsync(Profile, Connector, Operation, CancellationToken.None)
            .ConfigureAwait(false);

        OutputFormatter.WriteData(operation, PrintOperation);
        return ExitSuccess;
    }

    // Text-renderer callback invoked by OutputFormatter.WriteData — OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintOperation(OperationDetail operation)
    {
        OutputWriter.WriteLine($"{operation.OperationId} ({operation.Connector})");
        if (!string.IsNullOrWhiteSpace(operation.Summary))
            OutputWriter.WriteLine(operation.Summary);
        OutputWriter.WriteLine($"Action type: {operation.ActionType}");
        if (operation.IsDeprecated)
            OutputWriter.WriteLine("This operation is DEPRECATED — use its current replacement.");
        OutputWriter.WriteLine(string.Empty);

        if (operation.Parameters.Count == 0)
        {
            OutputWriter.WriteLine("This operation takes no parameters.");
            return;
        }

        var nameWidth = Math.Clamp(operation.Parameters.Max(p => p.Name.Length), 20, 45);
        var header = $"{"Parameter".PadRight(nameWidth)} | {"Type".PadRight(10)} | {"Req".PadRight(3)} | Description";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var parameter in operation.Parameters)
        {
            var name = parameter.Name.Length > nameWidth
                ? parameter.Name[..(nameWidth - 1)] + "."
                : parameter.Name;
            var type = (parameter.Type ?? "-").PadRight(10);
            var required = (parameter.Required ? "yes" : "no").PadRight(3);
            OutputWriter.WriteLine($"{name.PadRight(nameWidth)} | {type} | {required} | {parameter.Description}");

            if (parameter.AllowedValues is { Count: > 0 } allowed)
                OutputWriter.WriteLine($"{new string(' ', nameWidth)} | allowed: {string.Join(", ", allowed)}");

            // Without this the parameter looks free-form when in fact its
            // values, or its whole sub-schema, are resolved by another call.
            Describe("values from", parameter.DynamicValues);
            Describe("tree from", parameter.DynamicTree);
            Describe("sub-schema from", parameter.DynamicSchema);

            void Describe(string label, DynamicValuesRef? reference)
            {
                if (reference is null)
                    return;

                var arguments = reference.Parameters is { Count: > 0 } bag
                    ? " (" + string.Join(", ", bag.Select(kv => $"{kv.Key}={kv.Value}")) + ")"
                    : string.Empty;
                OutputWriter.WriteLine($"{new string(' ', nameWidth)} | {label}: {reference.OperationId}{arguments}");
            }
        }
    }
#pragma warning restore TXC003
}
