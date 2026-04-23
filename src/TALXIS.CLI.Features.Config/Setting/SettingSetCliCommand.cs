using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Config.Setting;

/// <summary>
/// <c>txc config setting set &lt;key&gt; &lt;value&gt;</c> — update one
/// tool-wide preference. Unknown keys are rejected (exit 2) so typos
/// never silently write garbage into <c>config.json</c>; values are
/// validated against the per-key whitelist in <see cref="SettingRegistry"/>.
/// </summary>
[CliCommand(
    Name = "set",
    Description = "Set a tool-wide preference key (e.g. log.level, log.format, telemetry.enabled)."
)]
public class SettingSetCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(SettingSetCliCommand));

    [CliArgument(Description = "Setting key (e.g. log.level).")]
    public required string Key { get; set; }

    [CliArgument(Description = "New value. Run 'txc config setting list' to see allowed values per key.")]
    public required string Value { get; set; }

    public async Task<int> RunAsync()
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            _logger.LogError("Setting key must be provided.");
            return 1;
        }

        var descriptor = SettingRegistry.Find(Key);
        if (descriptor is null)
        {
            _logger.LogError(
                "Unknown setting key '{Key}'. Known keys: {Keys}.",
                Key,
                string.Join(", ", SettingRegistry.All.Select(d => d.Key)));
            return 2;
        }

        string normalized;
        try
        {
            normalized = SettingRegistry.NormalizeValue(descriptor, Value);
        }
        catch (ArgumentException ex)
        {
            _logger.LogError("{Message}", ex.Message);
            return 2;
        }

        try
        {
            var store = TxcServices.Get<IGlobalConfigStore>();
            var config = await store.LoadAsync(CancellationToken.None).ConfigureAwait(false);
            descriptor.Write(config, normalized);
            await store.SaveAsync(config, CancellationToken.None).ConfigureAwait(false);

            _logger.LogInformation("Setting '{Key}' updated to '{Value}'.", descriptor.Key, normalized);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update setting '{Key}'.", descriptor.Key);
            return 1;
        }
    }
}
