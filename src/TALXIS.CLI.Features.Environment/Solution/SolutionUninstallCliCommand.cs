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
    Description = "Uninstall a managed solution and remove all its components from the environment."
)]
[CliDestructive("Permanently removes the managed solution and all its components from the environment.")]
public class SolutionUninstallCliCommand : ProfiledCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(SolutionUninstallCliCommand));

    [CliArgument(Name = "name", Description = "Solution unique name.")]
    public string Name { get; set; } = null!;

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation for this destructive operation.", Required = false)]
    public bool Yes { get; set; }

    [CliOption(Name = "--force", Description = "Proceed even if blocking dependencies are found.", Required = false)]
    public bool Force { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            Logger.LogError("'name' argument is required.");
            return ExitValidationError;
        }

        // Pre-check: block on dependencies unless --force is given
        var depService = TxcServices.Get<ISolutionDependencyService>();
        var deps = await depService.CheckUninstallAsync(Profile, Name, CancellationToken.None).ConfigureAwait(false);
        if (deps.Count > 0 && !Force)
        {
            Logger.LogError(
                "Solution '{Name}' has {Count} blocking dependency(ies). " +
                "Run 'txc env sln uninstall-check {Name}' to see details, or use --force to proceed anyway.",
                Name, deps.Count, Name);
            return ExitError;
        }
        if (deps.Count > 0 && Force)
        {
            Logger.LogWarning("Solution '{Name}' has {Count} blocking dependency(ies). Proceeding because --force was specified.", Name, deps.Count);
        }

        var service = TxcServices.Get<ISolutionUninstallService>();
        var outcome = await service.UninstallByUniqueNameAsync(Profile, Name, expectManaged: true, CancellationToken.None).ConfigureAwait(false);

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
