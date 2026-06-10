using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Plugin.Profile;

[CliIdempotent]
[CliCommand(
    Name = "enable",
    Description = "Enable plugin trace logging for the whole environment. Plugin execution is written to the plugintracelog table. Use --level all (default) to log everything, or --level exception to log only failures. Note: 'all' adds write overhead on busy environments."
)]
public class PluginProfileEnableCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(PluginProfileEnableCliCommand));

    [CliOption(Name = "--level", Description = "Trace level to set: all (default) or exception.", Required = false)]
    public string? Level { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (!PluginTraceLevelParser.TryParse(Level, PluginTraceLevel.All, out var level, out var error))
        {
            Logger.LogError("{Error}", error);
            return ExitValidationError;
        }

        var service = TxcServices.Get<IPluginTraceService>();
        var current = await service.GetSettingAsync(Profile, CancellationToken.None).ConfigureAwait(false);
        if (current.Level == level)
        {
            OutputFormatter.WriteResult("succeeded", $"Plugin trace logging is already set to {level}.");
            return ExitSuccess;
        }

        var updated = await service.SetSettingAsync(Profile, level, CancellationToken.None).ConfigureAwait(false);
        OutputFormatter.WriteResult("succeeded", $"Plugin trace logging set to {updated.Level}.");
        return ExitSuccess;
    }
}
