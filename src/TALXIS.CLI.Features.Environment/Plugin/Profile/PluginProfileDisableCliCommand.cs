using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Plugin.Profile;

[CliIdempotent]
[CliCommand(
    Name = "disable",
    Description = "Disable plugin trace logging for the whole environment (sets the trace level to Off)."
)]
public class PluginProfileDisableCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(PluginProfileDisableCliCommand));

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IPluginTraceService>();
        var current = await service.GetSettingAsync(Profile, CancellationToken.None).ConfigureAwait(false);
        if (current.Level == PluginTraceLevel.Off)
        {
            OutputFormatter.WriteResult("succeeded", "Plugin trace logging is already disabled.");
            return ExitSuccess;
        }

        await service.SetSettingAsync(Profile, PluginTraceLevel.Off, CancellationToken.None).ConfigureAwait(false);
        OutputFormatter.WriteResult("succeeded", "Plugin trace logging disabled.");
        return ExitSuccess;
    }
}
