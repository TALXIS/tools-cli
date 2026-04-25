using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Config.Setting;

/// <summary>
/// <c>txc config setting set &lt;key&gt; &lt;value&gt;</c> — update one
/// tool-wide preference. Unknown keys are rejected (exit 2) so typos
/// never silently write garbage into <c>config.json</c>; values are
/// validated against the per-key whitelist in <see cref="SettingRegistry"/>.
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "set",
    Description = "Set a tool-wide preference key (e.g. log.level, log.format, telemetry.enabled)."
)]
public class SettingSetCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(SettingSetCliCommand));

    [CliArgument(Description = "Setting key (e.g. log.level).")]
    public required string Key { get; set; }

    [CliArgument(Description = "New value. Run 'txc config setting list' to see allowed values per key.")]
    public required string Value { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            Logger.LogError("Setting key must be provided.");
            return ExitValidationError;
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

        var normalized = SettingRegistry.NormalizeValue(descriptor, Value);

        var store = TxcServices.Get<IGlobalConfigStore>();
        var config = await store.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        descriptor.Write(config, normalized);
        await store.SaveAsync(config, CancellationToken.None).ConfigureAwait(false);

        Logger.LogInformation("Setting '{Key}' updated to '{Value}'.", descriptor.Key, normalized);
        OutputFormatter.WriteResult("succeeded", $"Setting '{descriptor.Key}' set to '{normalized}'.");
        return ExitSuccess;
    }
}
