using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Governance.PolicyRule;

/// <summary>
/// Lists all tenant rule-based policies.
/// Usage: <c>txc governance policy-rule list</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List all rule-based policies in the tenant, with their id, name, and rule-set count."
)]
public class PolicyRuleListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(PolicyRuleListCliCommand));

    protected override async Task<int> ExecuteAsync()
    {
        var context = await PolicyRuleCommandSupport.ResolveContextAsync(Profile, CancellationToken.None).ConfigureAwait(false);
        var client = TxcServices.Get<IPowerPlatformPolicyRuleClient>();
        var policies = await client.ListAsync(context.Connection, context.Credential, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteList(policies, PolicyRuleOutput.PrintList);
        return ExitSuccess;
    }
}
