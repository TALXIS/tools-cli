using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.DependencyInjection;
using TALXIS.CLI.Config.Storage;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Shared;

namespace TALXIS.CLI.Config.Commands.Setting;

/// <summary>
/// <c>txc config setting list</c> — JSON dump of every whitelisted key,
/// its current value, and its allowed values. Intended as the one-stop
/// discovery surface so users don't have to hunt through docs to find
/// the supported keys.
/// </summary>
[CliCommand(
    Name = "list",
    Description = "List all known setting keys with current values as JSON."
)]
public class SettingListCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(SettingListCliCommand));

    public async Task<int> RunAsync()
    {
        try
        {
            var store = TxcServices.Get<IGlobalConfigStore>();
            var config = await store.LoadAsync(CancellationToken.None).ConfigureAwait(false);

            var projected = SettingRegistry.All.Select(d => new
            {
                key = d.Key,
                value = d.Read(config),
                description = d.Description,
                allowedValues = d.AllowedValues,
            });

            OutputWriter.WriteLine(JsonSerializer.Serialize(projected, TxcJsonOptions.Default));
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list settings.");
            return 1;
        }
    }
}
