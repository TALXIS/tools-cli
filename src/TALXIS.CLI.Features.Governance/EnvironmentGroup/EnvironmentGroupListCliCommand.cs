using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Governance.EnvironmentGroup;

/// <summary>
/// Lists all tenant environment groups.
/// Usage: <c>txc governance environment-group list</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List all environment groups in the tenant, with their id, display name, and member-environment count."
)]
public class EnvironmentGroupListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EnvironmentGroupListCliCommand));

    protected override async Task<int> ExecuteAsync()
    {
        var context = await EnvironmentGroupCommandSupport.ResolveContextAsync(Profile, CancellationToken.None).ConfigureAwait(false);
        var client = TxcServices.Get<IPowerPlatformEnvironmentGroupClient>();
        var groups = await client.ListAsync(context.Connection, context.Credential, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteList(groups, EnvironmentGroupOutput.PrintList);
        return ExitSuccess;
    }
}
