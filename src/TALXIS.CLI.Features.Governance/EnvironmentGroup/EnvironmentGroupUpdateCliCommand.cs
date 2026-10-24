using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Governance.EnvironmentGroup;

/// <summary>
/// Updates an existing environment group's display name and/or description.
/// Usage: <c>txc governance environment-group update &lt;environment-group&gt; [--display-name &lt;name&gt;] [--description &lt;text&gt;]</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "update",
    Description = "Update an environment group's display name and/or description. Only the fields you pass are changed."
)]
public class EnvironmentGroupUpdateCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EnvironmentGroupUpdateCliCommand));

    [CliArgument(Description = "Environment group id (GUID) or display name.")]
    public string EnvironmentGroup { get; set; } = string.Empty;

    [CliOption(Name = "--display-name", Description = "New display name.", Required = false)]
    public string? DisplayName { get; set; }

    [CliOption(Name = "--description", Description = "New description.", Required = false)]
    public string? Description { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (DisplayName is null && Description is null)
        {
            Logger.LogError("Nothing to update: pass --display-name and/or --description.");
            return ExitValidationError;
        }

        var context = await EnvironmentGroupCommandSupport.ResolveContextAsync(Profile, CancellationToken.None).ConfigureAwait(false);
        var existing = await EnvironmentGroupCommandSupport
            .ResolveAsync(context.Connection, context.Credential, EnvironmentGroup, CancellationToken.None)
            .ConfigureAwait(false);

        var client = TxcServices.Get<IPowerPlatformEnvironmentGroupClient>();
        await client.UpdateAsync(
            context.Connection,
            context.Credential,
            existing.Id,
            new PowerPlatformEnvironmentGroupUpdateOptions(DisplayName, Description),
            CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteResult("updated", id: existing.Id.ToString());
        return ExitSuccess;
    }
}
