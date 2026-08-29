using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Governance.EnvironmentGroup;

/// <summary>
/// Creates a new environment group.
/// Usage: <c>txc governance environment-group create --display-name &lt;name&gt; [--description &lt;text&gt;]</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "create",
    Description = "Create a new environment group. This is step 1 of the environment-group governance sequence: create the group, add member environments (txc governance environment-group environment add), then create and assign policy rules to it (txc governance policy-rule create / assign --environment-group)."
)]
public class EnvironmentGroupCreateCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EnvironmentGroupCreateCliCommand));

    [CliOption(Name = "--display-name", Description = "Display name for the new environment group.", Required = true)]
    public string DisplayName { get; set; } = string.Empty;

    [CliOption(Name = "--description", Description = "Optional description.", Required = false)]
    public string? Description { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var context = await EnvironmentGroupCommandSupport.ResolveContextAsync(Profile, CancellationToken.None).ConfigureAwait(false);
        var client = TxcServices.Get<IPowerPlatformEnvironmentGroupClient>();

        var group = await client.CreateAsync(
            context.Connection,
            context.Credential,
            new PowerPlatformEnvironmentGroupCreateOptions(DisplayName, Description),
            CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteResult("created", id: group.Id.ToString());
        return ExitSuccess;
    }
}
