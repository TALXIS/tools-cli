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
    Name = "delete",
    Description = "Delete an unmanaged solution container (components remain in the environment)."
)]
[CliDestructive("Removes the unmanaged solution container. Components are NOT deleted — they remain in the environment.")]
public class SolutionDeleteCliCommand : ProfiledCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(SolutionDeleteCliCommand));

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
        var outcome = await service.UninstallByUniqueNameAsync(Profile, Name, expectManaged: false, CancellationToken.None).ConfigureAwait(false);

        return RenderSingle(outcome);
    }

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
