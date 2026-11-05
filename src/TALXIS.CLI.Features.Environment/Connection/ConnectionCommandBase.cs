using DotMake.CommandLine;
using TALXIS.CLI.Features.Environment.Connector;

namespace TALXIS.CLI.Features.Environment.Connection;

/// <summary>
/// Shared base of the commands that act on one existing connection: the
/// connection id, and the optional connector that disambiguates it.
/// </summary>
public abstract class ConnectionCommandBase : ConnectivityCommandBase
{
    [CliArgument(Name = "connection-id", Description = "Connection id, exactly as shown by 'txc environment connection list'.")]
    public string ConnectionId { get; set; } = null!;

    [CliOption(Name = "--connector", Description = ConnectorDescriptions.Disambiguator, Required = false)]
    public string? Connector { get; set; }
}
