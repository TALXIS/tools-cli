using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Features.Config.Abstractions;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Setting;

/// <summary>
/// <c>txc environment setting update</c> — updates a single environment
/// management setting via the Power Platform control plane API.
/// </summary>
[CliCommand(
    Name = "update",
    Description = "Update an environment management setting (control plane)."
)]
public class SettingUpdateCliCommand : ProfiledCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(SettingUpdateCliCommand));

    [CliOption(Name = "--name", Aliases = new[] { "-n" }, Description = "Name of the setting to update (e.g. powerApps_AllowCodeApps).", Required = true)]
    public required string Name { get; set; }

    [CliOption(Name = "--value", Aliases = new[] { "-v" }, Description = "Value to set. Booleans (true/false) and integers are auto-coerced.", Required = true)]
    public required string Value { get; set; }

    public async Task<int> RunAsync()
    {
        try
        {
            var service = TxcServices.Get<IEnvironmentManagementSettingsService>();
            await service.UpdateAsync(Profile, Name, Value, CancellationToken.None)
                .ConfigureAwait(false);

            OutputWriter.WriteLine($"Setting '{Name}' updated to '{Value}'.");
            return 0;
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or NotSupportedException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "environment setting update failed");
            return 1;
        }
    }
}
