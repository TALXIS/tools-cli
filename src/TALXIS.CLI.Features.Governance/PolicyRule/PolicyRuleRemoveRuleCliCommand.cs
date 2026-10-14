using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Governance.PolicyRule;

/// <summary>
/// Removes one rule set from a policy. The policy itself is not deleted —
/// deleting a policy is not supported by the underlying API (see
/// <c>IPowerPlatformPolicyRuleClient</c> remarks).
/// Usage: <c>txc governance policy-rule remove-rule &lt;policy&gt; [--rule-set-id &lt;id&gt;]</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "remove-rule",
    Description = "Remove one rule set from a policy, identified by its rule set id (defaults to \"ConnectorManagement\"). The policy itself is not deleted; deleting a policy entirely is not supported by the Power Platform governance API as of this writing."
)]
public class PolicyRuleRemoveRuleCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(PolicyRuleRemoveRuleCliCommand));

    [CliArgument(Description = "Policy id (GUID) or name.")]
    public string Policy { get; set; } = string.Empty;

    [CliOption(Name = "--rule-set-id", Description = "Id of the rule set to remove. Defaults to \"ConnectorManagement\".", Required = false)]
    public string RuleSetId { get; set; } = PowerPlatformPolicyRuleSet.ConnectorManagementRuleSetId;

    protected override async Task<int> ExecuteAsync()
    {
        var context = await PolicyRuleCommandSupport.ResolveContextAsync(Profile, CancellationToken.None).ConfigureAwait(false);
        var existing = await PolicyRuleCommandSupport
            .ResolveAsync(context.Connection, context.Credential, Policy, CancellationToken.None)
            .ConfigureAwait(false);

        var client = TxcServices.Get<IPowerPlatformPolicyRuleClient>();
        await client.RemoveRuleSetAsync(context.Connection, context.Credential, existing.Id, RuleSetId, CancellationToken.None)
            .ConfigureAwait(false);

        OutputFormatter.WriteResult("removed", id: RuleSetId);
        return ExitSuccess;
    }
}
