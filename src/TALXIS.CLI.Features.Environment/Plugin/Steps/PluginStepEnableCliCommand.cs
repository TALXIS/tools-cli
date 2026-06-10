using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Plugin.Steps;

[CliIdempotent]
[CliCommand(
    Name = "enable",
    Description = "Enable (activate) a plugin processing step so it fires again. Accepts a step GUID or a step name (exact or unique substring). Common after importing a solution whose steps came in disabled."
)]
public class PluginStepEnableCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(PluginStepEnableCliCommand));

    [CliArgument(Name = "step", Description = "Step id (GUID) or name (exact or unique substring).")]
    public string Step { get; set; } = null!;

    [CliOption(Name = "--assembly", Description = "Narrow the search to steps whose owning assembly name contains this substring (helps disambiguate names).", Required = false)]
    public string? Assembly { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IPluginInventoryService>();
        var rows = await service.ListStepsAsync(Profile, Assembly, CancellationToken.None).ConfigureAwait(false);

        var resolution = PluginStepResolver.Resolve(rows, Step);
        if (resolution.Step is null)
        {
            Logger.LogError("{Error}", resolution.Error);
            return ExitValidationError;
        }

        var step = resolution.Step;
        if (step.Enabled)
        {
            OutputFormatter.WriteResult("succeeded", $"Step '{step.Name}' is already enabled.", step.Id.ToString());
            return ExitSuccess;
        }

        await service.SetStepStateAsync(Profile, step.Id, enabled: true, CancellationToken.None).ConfigureAwait(false);
        OutputFormatter.WriteResult("succeeded", $"Enabled step '{step.Name}'.", step.Id.ToString());
        return ExitSuccess;
    }
}
