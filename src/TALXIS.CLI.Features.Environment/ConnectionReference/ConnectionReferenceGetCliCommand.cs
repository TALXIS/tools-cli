using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.ConnectionReference;

/// <summary>
/// <c>txc environment connection-reference get</c> — one reference and its binding.
/// </summary>
[CliReadOnly]
[CliWorkflow("environment-inspection")]
[CliCommand(
    Name = "get",
    Description = "Gets one connection reference, including the connection it is bound to and that connection's health, in the LIVE connected environment. Requires an active profile."
)]
public class ConnectionReferenceGetCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectionReferenceGetCliCommand));

    [CliArgument(Name = "logical-name", Description = "Connection reference logical name.")]
    public string LogicalName { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IConnectionReferenceService>();
        var reference = await service.GetAsync(Profile, LogicalName, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteData(reference, PrintDetail);
        return ExitSuccess;
    }

    // Text-renderer callback invoked by OutputFormatter.WriteData — OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintDetail(ConnectionReferenceRecord r)
    {
        OutputWriter.WriteLine($"Logical name : {r.LogicalName}");
        OutputWriter.WriteLine($"Display name : {r.DisplayName}");
        OutputWriter.WriteLine($"Connector    : {r.ApiId}");
        OutputWriter.WriteLine($"Connection id: {r.ConnectionId ?? "(none)"}");
        OutputWriter.WriteLine($"Binding      : {r.BindingStatus}");
        if (!string.IsNullOrEmpty(r.BindingMessage))
            OutputWriter.WriteLine($"Note         : {r.BindingMessage}");
        OutputWriter.WriteLine($"Managed      : {r.IsManaged}");
        if (!string.IsNullOrEmpty(r.Owner))
            OutputWriter.WriteLine($"Owner        : {r.Owner}");
    }
#pragma warning restore TXC003
}
