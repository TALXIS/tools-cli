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
    Description = "Show one connector operation from the connected environment with its exact parameter names, types, enum values, and the operations that resolve dynamic parameters, for flow authoring."
)]
public class ConnectorOperationGetCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectorOperationGetCliCommand));

    [CliOption(Name = "--connector", Description = "Connector name as returned by 'connector list' (e.g. shared_commondataserviceforapps).", Required = true)]
    public string Connector { get; set; } = null!;

    [CliOption(Name = "--operation", Description = "Operation id as returned by 'connector operation list' (e.g. CreateRecord).", Required = true)]
    public string Operation { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IConnectorCatalogService>();
        ConnectorOperationDetail? detail = await service
            .GetOperationAsync(Profile, Connector, Operation, CancellationToken.None)
            .ConfigureAwait(false);

        if (detail is null)
        {
            Logger.LogError(
                "Operation '{Operation}' was not found on connector '{Connector}'. Use 'txc environment connector operation list --connector {Connector}' to see available operations.",
                Operation, Connector, Connector);
            return ExitValidationError;
        }

        OutputFormatter.WriteData(detail, PrintDetail);
        return ExitSuccess;
    }

    // Text-renderer callback invoked by OutputFormatter.WriteData, so OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintDetail(ConnectorOperationDetail d)
    {
        const int labelWidth = -14;
        OutputWriter.WriteLine($"{"Operation:",labelWidth}{d.OperationId}");
        OutputWriter.WriteLine($"{"Connector:",labelWidth}{d.ConnectorName}");
        OutputWriter.WriteLine($"{"Api Id:",labelWidth}{d.ApiId}");
        OutputWriter.WriteLine($"{"Kind:",labelWidth}{d.Kind}");
        OutputWriter.WriteLine($"{"Endpoint:",labelWidth}{d.HttpMethod} {d.Path}");
        if (!string.IsNullOrWhiteSpace(d.Summary))
            OutputWriter.WriteLine($"{"Summary:",labelWidth}{d.Summary}");

        OutputWriter.WriteLine(string.Empty);

        if (d.Parameters.Count == 0)
        {
            OutputWriter.WriteLine("No parameters.");
            return;
        }

        int nameWidth = Math.Clamp(d.Parameters.Max(p => p.Name.Length), 20, 60);
        int typeWidth = 18;
        string header = $"{"Parameter".PadRight(nameWidth)} | {"In".PadRight(6)} | {"Type".PadRight(typeWidth)} | {"Req".PadRight(3)} | {"Dyn".PadRight(3)} | Summary";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var p in d.Parameters)
        {
            string name = p.Name.Length > nameWidth ? p.Name[..(nameWidth - 1)] + "." : p.Name;
            string type = p.Type ?? string.Empty;
            type = type.Length > typeWidth ? type[..(typeWidth - 1)] + "." : type;
            string summary = p.Summary ?? string.Empty;
            if (p.EnumValues is { Count: > 0 })
                summary = $"{summary} [{string.Join(", ", p.EnumValues)}]".Trim();
            OutputWriter.WriteLine(
                $"{name.PadRight(nameWidth)} | {p.In.PadRight(6)} | {type.PadRight(typeWidth)} | {(p.Required ? "yes" : "").PadRight(3)} | {(p.IsDynamic ? "yes" : "").PadRight(3)} | {summary}");
            if (p.DynamicSource is not null)
                OutputWriter.WriteLine($"{string.Empty.PadRight(nameWidth)} | {FormatDynamicSource(p.DynamicSource)}");
        }
    }

    private static string FormatDynamicSource(ConnectorDynamicSource source)
    {
        string label = source.Kind == "schema" ? "sub-schema from" : "values from";
        string arguments = string.Join(", ", source.Arguments.Select(a => $"{a.Key}={a.Value}"));
        return arguments.Length == 0 ? $"{label}: {source.OperationId}" : $"{label}: {source.OperationId} ({arguments})";
    }
#pragma warning restore TXC003
}
