using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Governance;

/// <summary>
/// Top-level command for tenant-wide governance rules and configuration:
/// environment groups (folders that organize managed environments) and
/// rule-based policies (governance rules applied across groups/
/// environments, e.g. "Advanced connector policy"). See
/// <c>txc security</c> for identity/RBAC — that is a distinct concern from
/// governance, even though both are tenant-wide.
/// Usage: <c>txc governance [environment-group|policy-rule]</c>
/// </summary>
[CliCommand(
    Name = "governance",
    Description = "Discover and manage tenant-wide governance rules and configuration: environment groups and rule-based policies. See 'txc security' for tenant identity and role assignments.",
    Children = new[]
    {
        typeof(EnvironmentGroup.EnvironmentGroupCliCommand),
        typeof(PolicyRule.PolicyRuleCliCommand),
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class GovernanceCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
