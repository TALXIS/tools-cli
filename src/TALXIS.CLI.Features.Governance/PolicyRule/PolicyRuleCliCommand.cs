using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Governance.PolicyRule;

/// <summary>
/// Parent command for rule-based policies — the modern governance/policy
/// framework that is replacing classic DLP policies. Targets the confirmed
/// "Advanced Connector Policy" rule type; other rule types can be authored
/// via raw JSON once their shapes are confirmed by Microsoft.
/// Usage: <c>txc governance policy-rule [list|get|create|update|remove-rule|assign|assignment]</c>
/// </summary>
[CliCommand(
    Name = "policy-rule",
    Description = "Manage tenant-wide rule-based policies (the modern governance framework replacing classic DLP). Typical sequence: create a policy (optionally with a ConnectorManagement / Advanced Connector Policy rule set via --allow-connector), then assign it to an environment group or environment. Note: deleting a policy and unassigning it from a resource are not supported by the underlying Power Platform governance API as of this writing — see each command's description for the closest supported alternative.",
    Children = new[]
    {
        typeof(PolicyRuleListCliCommand),
        typeof(PolicyRuleGetCliCommand),
        typeof(PolicyRuleCreateCliCommand),
        typeof(PolicyRuleUpdateCliCommand),
        typeof(PolicyRuleRemoveRuleCliCommand),
        typeof(PolicyRuleAssignCliCommand),
        typeof(PolicyRuleAssignmentCliCommand),
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class PolicyRuleCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}

/// <summary>
/// Sub-resource for read-only visibility into policy assignments across
/// the tenant.
/// Usage: <c>txc governance policy-rule assignment list</c>
/// </summary>
[CliCommand(
    Name = "assignment",
    Description = "List policy assignments across the tenant, optionally filtered by policy, environment group, or environment.",
    Children = new[]
    {
        typeof(PolicyRuleAssignmentListCliCommand),
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class PolicyRuleAssignmentCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
