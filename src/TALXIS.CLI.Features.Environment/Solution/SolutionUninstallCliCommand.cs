using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;

namespace TALXIS.CLI.Features.Environment.Solution;

[CliCommand(
    Name = "uninstall",
    Description = "Uninstall a single solution by unique name from the target environment."
)]
[CliDestructive("Permanently uninstalls the solution and all its components from the remote environment.")]
public class SolutionUninstallCliCommand : ProfiledCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(SolutionUninstallCliCommand));

    [CliArgument(Name = "name", Description = "Solution unique name.")]
    public required string Name { get; set; }

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation for this destructive operation.", Required = false)]
    public bool Yes { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            Logger.LogError("'name' argument is required.");
            return ExitValidationError;
        }

        var service = TxcServices.Get<ISolutionUninstallService>();
        var outcome = await service.UninstallByUniqueNameAsync(Profile, Name, CancellationToken.None).ConfigureAwait(false);

        return RenderSingle(outcome);
    }

    // TODO: Refactor to use OutputFormatter instead of manual OutputContext.IsJson branching.
#pragma warning disable TXC003
    private int RenderSingle(SolutionUninstallOutcome outcome)
    {
        if (OutputContext.IsJson)
        {
            OutputWriter.WriteLine(JsonSerializer.Serialize(new
            {
                mode = "solution",
                outcome,
            }, TxcOutputJsonOptions.Default));
        }
        else
        {
            OutputWriter.WriteLine($"Solution: {outcome.SolutionName}");
            OutputWriter.WriteLine($"  status: {outcome.Status}");
            if (outcome.SolutionId is { } id)
            {
                OutputWriter.WriteLine($"  id: {id}");
            }
            OutputWriter.WriteLine($"  message: {outcome.Message}");
        }

        return outcome.Status == SolutionUninstallStatus.Success ? ExitSuccess : ExitError;
    }
#pragma warning restore TXC003
}
