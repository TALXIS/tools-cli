using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Governance.EnvironmentGroup;

/// <summary>
/// Removes a managed environment from an environment group.
/// Usage: <c>txc governance environment-group environment remove &lt;environment-group&gt; --environment &lt;id&gt;</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "remove",
    Description = "Remove a managed environment from an environment group. The environment retains the last-applied configuration from the group's rules but becomes unlocked, allowing a local admin to modify it going forward."
)]
public class EnvironmentGroupEnvironmentRemoveCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EnvironmentGroupEnvironmentRemoveCliCommand));

    [CliArgument(Description = "Environment group id (GUID) or display name.")]
    public string EnvironmentGroup { get; set; } = string.Empty;

    [CliOption(Name = "--environment", Description = "Id (GUID) of the environment to remove.", Required = true)]
    public Guid Environment { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var context = await EnvironmentGroupCommandSupport.ResolveContextAsync(Profile, CancellationToken.None).ConfigureAwait(false);
        var group = await EnvironmentGroupCommandSupport
            .ResolveAsync(context.Connection, context.Credential, EnvironmentGroup, CancellationToken.None)
            .ConfigureAwait(false);

        var client = TxcServices.Get<IPowerPlatformEnvironmentGroupClient>();
        await client.RemoveEnvironmentAsync(context.Connection, context.Credential, group.Id, Environment, CancellationToken.None)
            .ConfigureAwait(false);

        OutputFormatter.WriteResult("removed", id: Environment.ToString());
        return ExitSuccess;
    }
}
