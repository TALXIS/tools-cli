using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

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
    public string Name { get; set; } = null!;

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation for this destructive operation.", Required = false)]
    public bool Yes { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<ISolutionUninstallService>();
        var outcome = await service.UninstallByUniqueNameAsync(Profile, Name, expectManaged: false, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteData(outcome, _ =>
        {
#pragma warning disable TXC003
            OutputWriter.WriteLine($"Solution: {outcome.SolutionName}");
            OutputWriter.WriteLine($"  status: {outcome.Status}");
            if (outcome.SolutionId is { } id)
                OutputWriter.WriteLine($"  id: {id}");
            OutputWriter.WriteLine($"  message: {outcome.Message}");
#pragma warning restore TXC003
        });

        return outcome.Status == SolutionUninstallStatus.Success ? ExitSuccess : ExitError;
    }
}
