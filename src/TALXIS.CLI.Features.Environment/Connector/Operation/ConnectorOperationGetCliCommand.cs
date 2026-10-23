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

    // Text-renderer callback invoked by OutputFormatter.WriteData.
    private static void PrintOperation(OperationDetail operation) => OperationTextRenderer.Print(operation);
}
