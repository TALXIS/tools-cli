using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Connection;

/// <summary>
/// <c>txc environment connection get</c> — one connection and its platform status.
/// </summary>
[CliReadOnly]
[CliWorkflow("environment-inspection")]
[CliCommand(
    Name = "get",
    Description = "Gets one connection, including its platform status, in the LIVE connected environment. Requires an active profile."
)]
public class ConnectionGetCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectionGetCliCommand));

    [CliArgument(Name = "connection-id", Description = "Connection id, exactly as shown by 'environment connection list'.")]
    public string ConnectionId { get; set; } = null!;

    [CliOption(Name = "--connector", Description = "Connector of the connection (logical name, apiId or exact display name).", Required = true)]
    public string Connector { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IConnectionService>();
        var connection = await service.GetAsync(Profile, Connector, ConnectionId, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteData(connection, PrintDetail);
        return ExitSuccess;
    }

    // Text-renderer callback invoked by OutputFormatter.WriteData — OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintDetail(ConnectionSummary c)
    {
        OutputWriter.WriteLine($"Display name : {c.DisplayName}");
        OutputWriter.WriteLine($"Connection id: {c.ConnectionId}");
        OutputWriter.WriteLine($"Connector    : {c.ApiId}");
        OutputWriter.WriteLine($"Status       : {c.ConnectionStatus}");
        if (!string.IsNullOrEmpty(c.StatusMessage))
            OutputWriter.WriteLine($"Message      : {c.StatusMessage}");
        if (!string.IsNullOrEmpty(c.CreatedBy))
            OutputWriter.WriteLine($"Created by   : {c.CreatedBy} ({c.CreatedByType})");
        if (c.CreatedTime is { } created)
            OutputWriter.WriteLine($"Created      : {created:u}");
    }
#pragma warning restore TXC003
}
