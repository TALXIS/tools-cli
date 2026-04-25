using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Config.Setting;

/// <summary>
/// <c>txc config setting get &lt;key&gt;</c> — print the current value of
/// one whitelisted setting to stdout. Unknown keys exit 2 with a hint
/// listing the known keys so shell scripts can distinguish "empty"
/// (default) from "typo".
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "get",
    Description = "Get a tool-wide preference value by key."
)]
public class SettingGetCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(SettingGetCliCommand));

    [CliArgument(Description = "Setting key (e.g. log.level).")]
    public required string Key { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            Logger.LogError("Setting key must be provided.");
            return ExitError;
        }

        var descriptor = SettingRegistry.Find(Key);
        if (descriptor is null)
        {
            Logger.LogError(
                "Unknown setting key '{Key}'. Known keys: {Keys}.",
                Key,
                string.Join(", ", SettingRegistry.All.Select(d => d.Key)));
            return ExitValidationError;
        }

        var store = TxcServices.Get<IGlobalConfigStore>();
        var config = await store.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        OutputFormatter.WriteValue(descriptor.Key, descriptor.Read(config));
        return ExitSuccess;
    }
}
