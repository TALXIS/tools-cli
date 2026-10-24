using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Governance.EnvironmentGroup;

/// <summary>
/// Adds a managed environment to an environment group.
/// Usage: <c>txc governance environment-group environment add &lt;environment-group&gt; --environment &lt;id&gt;</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "add",
    Description = "Add a managed environment to an environment group. The environment must not already belong to another group (each environment can belong to at most one group). The environment immediately inherits every rule published on this group."
)]
public class EnvironmentGroupEnvironmentAddCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EnvironmentGroupEnvironmentAddCliCommand));

    [CliArgument(Description = "Environment group id (GUID) or display name.")]
    public string EnvironmentGroup { get; set; } = string.Empty;

    [CliOption(Name = "--environment", Description = "Id (GUID) of the environment to add.", Required = true)]
    public Guid Environment { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var context = await EnvironmentGroupCommandSupport.ResolveContextAsync(Profile, CancellationToken.None).ConfigureAwait(false);
        var group = await EnvironmentGroupCommandSupport
            .ResolveAsync(context.Connection, context.Credential, EnvironmentGroup, CancellationToken.None)
            .ConfigureAwait(false);

        var client = TxcServices.Get<IPowerPlatformEnvironmentGroupClient>();
        await client.AddEnvironmentAsync(context.Connection, context.Credential, group.Id, Environment, CancellationToken.None)
            .ConfigureAwait(false);

        OutputFormatter.WriteResult("added", id: Environment.ToString());
        return ExitSuccess;
    }
}
