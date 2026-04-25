using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Features.Config.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Core;

namespace TALXIS.CLI.Features.Environment.Solution;

[CliCommand(
    Name = "uninstall",
    Description = "Uninstall a single solution by unique name from the target environment."
)]
public class SolutionUninstallCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(SolutionUninstallCliCommand));

    [CliArgument(Name = "name", Description = "Solution unique name.", Required = true)]
    public required string Name { get; set; }

    [CliOption(Name = "--yes", Description = "Confirm destructive uninstall action.", Required = false)]
    public bool Yes { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (!Yes)
        {
            Logger.LogError("Uninstall is destructive. Pass --yes to confirm.");
            return ExitValidationError;
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            Logger.LogError("'name' argument is required.");
            return ExitValidationError;
        }

        var service = TxcServices.Get<ISolutionUninstallService>();
        var outcome = await service.UninstallByUniqueNameAsync(Profile, Name, CancellationToken.None).ConfigureAwait(false);

        return RenderSingle(outcome);
    }

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
}
