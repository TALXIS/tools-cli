using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Config.Connection;

/// <summary>
/// <c>txc config connection</c> — Connections are the "where": service
/// endpoint metadata (URLs, tenant ids, org ids) that's identity-neutral
/// and can be safely committed by teams who share environments.
/// V1 ships the Dataverse provider only — <c>--provider</c> accepts
/// <c>dataverse</c> and rejects all other values with an explicit
/// "not implemented in v1" error (see plan §provider-stubs).
/// </summary>
[CliCommand(
    Name = "connection",
    Description = "Manage service endpoint metadata (Dataverse environments, etc.).",
    Children = new[]
    {
        typeof(ConnectionCreateCliCommand),
        typeof(ConnectionListCliCommand),
        typeof(ConnectionShowCliCommand),
        typeof(ConnectionDeleteCliCommand),
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class ConnectionCliCommand
{
    public void Run(CliContext context) => context.ShowHelp();
}
