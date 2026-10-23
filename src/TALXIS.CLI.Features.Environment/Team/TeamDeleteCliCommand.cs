using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Team;

/// <summary>
/// Deletes a Dataverse team.
/// Usage: <c>txc environment team delete --team &lt;name-or-guid&gt; --yes</c>
/// </summary>
[CliDestructive("Permanently deletes the Dataverse team.")]
[CliCommand(
    Name = "delete",
    Description = "Delete a Dataverse team by exact name or GUID. This is destructive."
)]
public class TeamDeleteCliCommand : ProfiledCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(TeamDeleteCliCommand));

    [CliOption(Name = "--team", Description = "Exact team name or team GUID.", Required = true)]
    public string Team { get; set; } = null!;

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation.", Required = false)]
    public bool Yes { get; set; }

    protected override Task<int> ExecuteAsync() => ExecuteDeleteAsync();

    private async Task<int> ExecuteDeleteAsync()
    {
        var service = TxcServices.Get<IDataverseTeamService>();

        try
        {
            await service.DeleteAsync(Profile, Team, CancellationToken.None).ConfigureAwait(false);
            OutputFormatter.WriteResult("succeeded", $"Dataverse team '{Team}' deleted.");
            return ExitSuccess;
        }
        catch (DataverseAmbiguousMatchException ex)
        {
            return TeamCommandSupport.HandleDataverseValidationException(Logger, ex, ExitValidationError);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("was not found.", StringComparison.Ordinal))
        {
            return TeamCommandSupport.HandleDataverseValidationException(Logger, ex, ExitValidationError);
        }
    }
}
