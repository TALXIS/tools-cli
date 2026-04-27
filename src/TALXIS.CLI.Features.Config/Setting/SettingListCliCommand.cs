using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Config.Setting;

/// <summary>
/// <c>txc config setting list</c> — JSON dump of every whitelisted key,
/// its current value, and its allowed values. Intended as the one-stop
/// discovery surface so users don't have to hunt through docs to find
/// the supported keys.
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "Lists all LOCAL CLI preference keys with current values as JSON. For LIVE Dataverse environment settings, use 'environment setting list' instead."
)]
public class SettingListCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(SettingListCliCommand));

    protected override async Task<int> ExecuteAsync()
    {
        var store = TxcServices.Get<IGlobalConfigStore>();
        var config = await store.LoadAsync(CancellationToken.None).ConfigureAwait(false);

        var projected = SettingRegistry.All.Select(d => new
        {
            key = d.Key,
            value = d.Read(config),
            description = d.Description,
            allowedValues = d.AllowedValues,
        }).ToList();

        OutputFormatter.WriteList(projected);
        return ExitSuccess;
    }
}
