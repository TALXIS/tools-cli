using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.DependencyInjection;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Shared;

namespace TALXIS.CLI.Config.Commands.Setting;

/// <summary>
/// <c>txc config setting get &lt;key&gt;</c> — print the current value of
/// one whitelisted setting to stdout. Unknown keys exit 2 with a hint
/// listing the known keys so shell scripts can distinguish "empty"
/// (default) from "typo".
/// </summary>
[CliCommand(
    Name = "get",
    Description = "Get a tool-wide preference value by key."
)]
public class SettingGetCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(SettingGetCliCommand));

    [CliArgument(Description = "Setting key (e.g. log.level).")]
    public required string Key { get; set; }

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

        try
        {
            var store = TxcServices.Get<IGlobalConfigStore>();
            var config = await store.LoadAsync(CancellationToken.None).ConfigureAwait(false);
            OutputWriter.WriteLine(descriptor.Read(config));
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read setting '{Key}'.", descriptor.Key);
            return 1;
        }
    }
}
