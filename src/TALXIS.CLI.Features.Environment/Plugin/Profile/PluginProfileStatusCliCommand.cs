using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Plugin.Profile;

[CliReadOnly]
[CliCommand(
    Name = "status",
    Description = "Show the current organization-wide plugin trace log level (Off, Exception, or All)."
)]
public class PluginProfileStatusCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(PluginProfileStatusCliCommand));

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IPluginTraceService>();
        var setting = await service.GetSettingAsync(Profile, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteData(setting, PrintStatus);
        return ExitSuccess;
    }

    // Text-renderer callback invoked by OutputFormatter.WriteData — OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintStatus(PluginTraceSetting setting)
    {
        var org = string.IsNullOrWhiteSpace(setting.OrganizationName) ? setting.OrganizationId.ToString() : setting.OrganizationName;
        OutputWriter.WriteLine($"Plugin trace log level: {setting.Level} (organization: {org})");
    }
#pragma warning restore TXC003
}
