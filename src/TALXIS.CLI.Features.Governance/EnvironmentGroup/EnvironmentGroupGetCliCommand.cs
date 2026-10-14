using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Governance.EnvironmentGroup;

/// <summary>
/// Gets a single environment group by id or display name.
/// Usage: <c>txc governance environment-group get &lt;environment-group&gt;</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "get",
    Description = "Get a single environment group by id or display name."
)]
public class EnvironmentGroupGetCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EnvironmentGroupGetCliCommand));

    [CliArgument(Description = "Environment group id (GUID) or display name.")]
    public string EnvironmentGroup { get; set; } = string.Empty;

    protected override async Task<int> ExecuteAsync()
    {
        var context = await EnvironmentGroupCommandSupport.ResolveContextAsync(Profile, CancellationToken.None).ConfigureAwait(false);
        var group = await EnvironmentGroupCommandSupport
            .ResolveAsync(context.Connection, context.Credential, EnvironmentGroup, CancellationToken.None)
            .ConfigureAwait(false);

        OutputFormatter.WriteData(group, EnvironmentGroupOutput.PrintDetail);
        return ExitSuccess;
    }
}
