using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Config.Profile;

/// <summary>
/// <c>txc config profile select &lt;name&gt;</c> — set the global
/// active-profile pointer in <c>${TXC_CONFIG_DIR}/config.json</c>.
/// Layered precedence in <see cref="Config.Resolution.ConfigurationResolver"/>
/// means <c>TXC_PROFILE</c> and <c>.txc/workspace.json</c> still win
/// per invocation; <c>select</c> only changes the fallback.
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "select",
    Description = "Set the global active profile (fallback when no --profile / env / workspace override)."
)]
public class ProfileSelectCliCommand : TxcLeafCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(ProfileSelectCliCommand));
    protected override ILogger Logger => _logger;

    [CliArgument(Description = "Profile name to set as active.")]
    public required string Name { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            _logger.LogError("Profile name must be provided.");
            return ExitError;
        }

        var profileStore = TxcServices.Get<IProfileStore>();
        var globalConfig = TxcServices.Get<IGlobalConfigStore>();

        var profile = await profileStore.GetAsync(Name, CancellationToken.None).ConfigureAwait(false);
        if (profile is null)
        {
            _logger.LogError("Profile '{Name}' not found.", Name);
            return ExitValidationError;
        }

        var global = await globalConfig.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        global.ActiveProfile = profile.Id;
        await globalConfig.SaveAsync(global, CancellationToken.None).ConfigureAwait(false);

        _logger.LogInformation("Active profile set to '{Id}'.", profile.Id);
        OutputFormatter.WriteResult("succeeded", $"Active profile set to '{profile.Id}'.");
        return ExitSuccess;
    }
}
