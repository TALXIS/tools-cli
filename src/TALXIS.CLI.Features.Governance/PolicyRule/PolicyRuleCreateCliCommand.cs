using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Governance.PolicyRule;

/// <summary>
/// Creates a new rule-based policy, optionally with one rule set attached.
/// Usage: <c>txc governance policy-rule create --name &lt;name&gt;
/// [--allow-connector &lt;id&gt;[=action1,action2] ... | --rule-set-inputs-json &lt;json&gt;]
/// [--rule-set-id &lt;id&gt;] [--rule-set-version &lt;version&gt;]</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "create",
    Description = "Create a new rule-based policy. This is step 1 of the policy governance sequence: create the policy (optionally with its first rule set), then assign it to an environment group or environment (txc governance policy-rule assign). Use --allow-connector for the confirmed Advanced Connector Policy rule type; use --rule-set-inputs-json for any other rule type once its shape is known."
)]
public class PolicyRuleCreateCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(PolicyRuleCreateCliCommand));

    [CliOption(Name = "--name", Description = "Name for the new policy.", Required = true)]
    public string Name { get; set; } = string.Empty;

    [CliOption(Name = "--rule-set-id", Description = "Rule set type discriminator. Defaults to \"ConnectorManagement\" (the only confirmed rule type: Advanced Connector Policy).", Required = false)]
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

        var context = await PolicyRuleCommandSupport.ResolveContextAsync(Profile, CancellationToken.None).ConfigureAwait(false);
        var client = TxcServices.Get<IPowerPlatformPolicyRuleClient>();

        var ruleSets = ruleSet is null
            ? Array.Empty<PowerPlatformPolicyRuleSet>()
            : new[] { ruleSet };

        var policy = await client.CreateAsync(
            context.Connection,
            context.Credential,
            new PowerPlatformPolicyCreateOptions(Name, ruleSets),
            CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteResult("created", id: policy.Id.ToString());
        return ExitSuccess;
    }
}
