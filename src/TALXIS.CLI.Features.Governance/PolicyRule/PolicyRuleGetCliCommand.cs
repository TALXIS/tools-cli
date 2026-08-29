using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Governance.PolicyRule;

/// <summary>
/// Gets a single rule-based policy by id or name.
/// Usage: <c>txc governance policy-rule get &lt;policy&gt;</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "get",
    Description = "Get a single rule-based policy by id or name, including its rule sets."
)]
public class PolicyRuleGetCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(PolicyRuleGetCliCommand));

    [CliArgument(Description = "Policy id (GUID) or name.")]
    public string Policy { get; set; } = string.Empty;

    protected override async Task<int> ExecuteAsync()
    {
        var context = await PolicyRuleCommandSupport.ResolveContextAsync(Profile, CancellationToken.None).ConfigureAwait(false);
        var policy = await PolicyRuleCommandSupport
            .ResolveAsync(context.Connection, context.Credential, Policy, CancellationToken.None)
            .ConfigureAwait(false);

        OutputFormatter.WriteData(policy, PolicyRuleOutput.PrintDetail);
        return ExitSuccess;
    }
}
