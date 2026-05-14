using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Setting;

/// <summary>
/// <c>txc environment setting update</c> — updates a single environment
/// setting. The correct backend is resolved automatically.
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "set",
    Description = "Sets a Dataverse environment-level setting on the LIVE connected environment. Requires an active profile. For LOCAL CLI preferences, use 'config setting set' instead."
)]
public class SettingSetCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(SettingSetCliCommand));

    [CliArgument(Description = "Name of the setting to set (e.g. PowerApps_AllowCodeApps, isauditenabled).")]
    public required string Name { get; set; }

    [CliArgument(Description = "Value to set. Booleans (true/false) and integers are auto-coerced.")]
    public required string Value { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IEnvironmentSettingsService>();
        await service.UpdateAsync(Profile, Name, Value, CancellationToken.None)
            .ConfigureAwait(false);

        OutputFormatter.WriteResult("succeeded", $"Setting '{Name}' updated to '{Value}'.");
        return ExitSuccess;
    }
}
