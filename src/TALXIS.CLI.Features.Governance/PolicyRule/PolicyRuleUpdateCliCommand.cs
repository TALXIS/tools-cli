using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Governance.PolicyRule;

/// <summary>
/// Renames a policy and/or adds or updates one rule set on it. Additive:
/// existing rule sets not targeted by this call are left untouched. Use
/// <c>remove-rule</c> to remove a rule set entirely.
/// Usage: <c>txc governance policy-rule update &lt;policy&gt; [--name &lt;name&gt;]
/// [--allow-connector &lt;id&gt;[=action1,action2] ... | --rule-set-inputs-json &lt;json&gt;]
/// [--rule-set-id &lt;id&gt;] [--rule-set-version &lt;version&gt;]</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "update",
    Description = "Rename a policy and/or add or update one rule set on it. Additive: existing rule sets not targeted by this call are left untouched. Use 'remove-rule' to remove a rule set entirely."
)]
public class PolicyRuleUpdateCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(PolicyRuleUpdateCliCommand));

    [CliArgument(Description = "Policy id (GUID) or name.")]
    public string Policy { get; set; } = string.Empty;

    [CliOption(Name = "--name", Description = "New name for the policy.", Required = false)]
    public string? Name { get; set; }

    [CliOption(Name = "--rule-set-id", Description = "Rule set type discriminator to add or update. Defaults to \"ConnectorManagement\" (the only confirmed rule type: Advanced Connector Policy).", Required = false)]
    public string RuleSetId { get; set; } = PowerPlatformPolicyRuleSet.ConnectorManagementRuleSetId;

    [CliOption(Name = "--rule-set-version", Description = "Rule set version.", Required = false)]
    public string RuleSetVersion { get; set; } = "1";

    [CliOption(Name = "--allow-connector", Description = "Allow-list one connector for the ConnectorManagement (Advanced Connector Policy) rule set. Repeatable. Format: connectorId (allow every action) or connectorId=action1,action2 (allow only the listed actions). Connectors not listed are blocked by default.", Required = false)]
    public string[]? AllowConnector { get; set; }

    [CliOption(Name = "--rule-set-inputs-json", Description = "Raw JSON for the rule set's \"inputs\" object. Use this instead of --allow-connector for rule types other than ConnectorManagement.", Required = false)]
    public string? RuleSetInputsJson { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var ruleSet = PolicyRuleCommandSupport.BuildRuleSet(RuleSetId, RuleSetVersion, RuleSetInputsJson, AllowConnector);

        if (Name is null && ruleSet is null)
        {
            Logger.LogError("Nothing to update: pass --name and/or a rule set (--allow-connector or --rule-set-inputs-json).");
            return ExitValidationError;
        }

        var context = await PolicyRuleCommandSupport.ResolveContextAsync(Profile, CancellationToken.None).ConfigureAwait(false);
        var existing = await PolicyRuleCommandSupport
            .ResolveAsync(context.Connection, context.Credential, Policy, CancellationToken.None)
            .ConfigureAwait(false);

        var client = TxcServices.Get<IPowerPlatformPolicyRuleClient>();
        var ruleSets = ruleSet is null ? null : new[] { ruleSet };

        await client.UpdateAsync(
            context.Connection,
            context.Credential,
            existing.Id,
            new PowerPlatformPolicyPatchOptions(Name, ruleSets),
            CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteResult("updated", id: existing.Id.ToString());
        return ExitSuccess;
    }
}
