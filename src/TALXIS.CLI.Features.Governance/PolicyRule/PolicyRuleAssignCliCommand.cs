using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Governance.PolicyRule;

/// <summary>
/// Assigns a policy to an environment group or a single environment.
/// Exactly one of <c>--environment-group</c>/<c>--environment</c> must be
/// supplied. Unassigning is not supported by the underlying API (see
/// <c>IPowerPlatformPolicyRuleClient</c> remarks) — to stop enforcing a
/// policy on one member of a group, exclude it with <c>--exclude-environment</c>
/// on a group-wide assignment instead.
/// Usage: <c>txc governance policy-rule assign &lt;policy&gt; (--environment-group &lt;id&gt; | --environment &lt;id&gt;) [--exclude-environment &lt;id&gt; ...]</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "assign",
    Description = "Assign a policy to an environment group or a single environment. Exactly one of --environment-group/--environment is required. When assigning to a group, use --exclude-environment (repeatable) to exempt specific member environments. Unassigning a policy is not supported by the Power Platform governance API as of this writing; to stop enforcing a group-wide policy on one environment, reassign with that environment added to --exclude-environment."
)]
public class PolicyRuleAssignCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(PolicyRuleAssignCliCommand));

    [CliArgument(Description = "Policy id (GUID) or name.")]
    public string Policy { get; set; } = string.Empty;

    [CliOption(Name = "--environment-group", Description = "Id (GUID) of the environment group to assign the policy to. Mutually exclusive with --environment.", Required = false)]
    public Guid? EnvironmentGroup { get; set; }

    [CliOption(Name = "--environment", Description = "Id (GUID) of the single environment to assign the policy to. Mutually exclusive with --environment-group.", Required = false)]
    public Guid? Environment { get; set; }

    [CliOption(Name = "--exclude-environment", Description = "Id (GUID) of a member environment to exempt from a group-wide assignment. Repeatable. Only valid with --environment-group.", Required = false)]
    public Guid[]? ExcludeEnvironment { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (EnvironmentGroup is null == (Environment is null))
        {
            Logger.LogError("Specify exactly one of --environment-group or --environment.");
            return ExitValidationError;
        }

        if (Environment is not null && ExcludeEnvironment is { Length: > 0 })
        {
            Logger.LogError("--exclude-environment is only valid with --environment-group.");
            return ExitValidationError;
        }

        var context = await PolicyRuleCommandSupport.ResolveContextAsync(Profile, CancellationToken.None).ConfigureAwait(false);
        var policy = await PolicyRuleCommandSupport
            .ResolveAsync(context.Connection, context.Credential, Policy, CancellationToken.None)
            .ConfigureAwait(false);

        var client = TxcServices.Get<IPowerPlatformPolicyRuleClient>();

        if (EnvironmentGroup is { } groupId)
        {
            var overrides = ExcludeEnvironment?
                .Select(id => new PowerPlatformPolicyAssignmentOverride(
                    PowerPlatformPolicyBehaviorType.Exclude, id, PowerPlatformPolicyAssignmentResourceType.Environment))
                .ToList();

            await client.AssignToEnvironmentGroupAsync(context.Connection, context.Credential, policy.Id, groupId, overrides, CancellationToken.None)
                .ConfigureAwait(false);

            OutputFormatter.WriteResult("assigned", id: groupId.ToString());
        }
        else
        {
            await client.AssignToEnvironmentAsync(context.Connection, context.Credential, policy.Id, Environment!.Value, null, CancellationToken.None)
                .ConfigureAwait(false);

            OutputFormatter.WriteResult("assigned", id: Environment!.Value.ToString());
        }

        return ExitSuccess;
    }
}
