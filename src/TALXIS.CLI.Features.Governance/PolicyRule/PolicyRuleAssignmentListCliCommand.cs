using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Governance.PolicyRule;

/// <summary>
/// Lists policy assignments, optionally filtered to exactly one of policy,
/// environment group, or environment.
/// Usage: <c>txc governance policy-rule assignment list [--policy &lt;id&gt; | --environment-group &lt;id&gt; | --environment &lt;id&gt;]</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List policy assignments. With no filter, lists every assignment in the tenant. Pass at most one of --policy/--environment-group/--environment to narrow the list to one dimension."
)]
public class PolicyRuleAssignmentListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(PolicyRuleAssignmentListCliCommand));

    [CliOption(Name = "--policy", Description = "Id (GUID) of a policy to filter by. Mutually exclusive with --environment-group/--environment.", Required = false)]
    public Guid? Policy { get; set; }

    [CliOption(Name = "--environment-group", Description = "Id (GUID) of an environment group to filter by. Mutually exclusive with --policy/--environment.", Required = false)]
    public Guid? EnvironmentGroup { get; set; }

    [CliOption(Name = "--environment", Description = "Id (GUID) of an environment to filter by. Mutually exclusive with --policy/--environment-group.", Required = false)]
    public Guid? Environment { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        int filterCount = new[] { Policy, EnvironmentGroup, Environment }.Count(f => f is not null);
        if (filterCount > 1)
        {
            Logger.LogError("Specify at most one of --policy, --environment-group, or --environment.");
            return ExitValidationError;
        }

        var context = await PolicyRuleCommandSupport.ResolveContextAsync(Profile, CancellationToken.None).ConfigureAwait(false);
        var client = TxcServices.Get<IPowerPlatformPolicyRuleClient>();

        var assignments = await client.ListAssignmentsAsync(
            context.Connection, context.Credential, Policy, EnvironmentGroup, Environment, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteList(assignments, PolicyRuleOutput.PrintAssignmentList);
        return ExitSuccess;
    }
}
