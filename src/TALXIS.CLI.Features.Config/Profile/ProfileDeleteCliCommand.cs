using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Config.Profile;

/// <summary>
/// <c>txc config profile delete &lt;name&gt;</c> — remove a profile.
///
/// <para>
/// Default behaviour keeps dependents (the linked credential and
/// connection) so other profiles aren't broken; pass <c>--cascade</c>
/// to also remove them IF no other profile references them. The
/// active-profile pointer is cleared when the deleted profile was
/// active — scripts that rely on an active profile then fail fast
/// instead of silently resolving to a deleted one.
/// </para>
/// </summary>
[CliDestructive("Deletes the profile and optionally cascades to its credential and connection.")]
[CliCommand(
    Name = "delete",
    Description = "Delete a profile. Dependents are kept unless --cascade."
)]
public class ProfileDeleteCliCommand : TxcLeafCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(ProfileDeleteCliCommand));
    protected override ILogger Logger => _logger;

    [CliArgument(Description = "Profile name.")]
    public required string Name { get; set; }

    [CliOption(Name = "--cascade", Description = "Also delete the linked auth + connection (only if no other profile uses them).", Required = false)]
    public bool Cascade { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            _logger.LogError("Profile name must be provided.");
            return ExitError;
        }

        var profileStore = TxcServices.Get<IProfileStore>();
        var connectionStore = TxcServices.Get<IConnectionStore>();
        var credentialStore = TxcServices.Get<ICredentialStore>();
        var globalConfig = TxcServices.Get<IGlobalConfigStore>();
        var vault = TxcServices.Get<ICredentialVault>();

        var existing = await profileStore.GetAsync(Name, CancellationToken.None).ConfigureAwait(false);
        if (existing is null)
        {
            _logger.LogError("Profile '{Name}' not found.", Name);
            return ExitValidationError;
        }

        var removed = await profileStore.DeleteAsync(existing.Id, CancellationToken.None).ConfigureAwait(false);
        if (!removed)
        {
            _logger.LogError("Profile '{Id}' disappeared during delete.", existing.Id);
            return ExitError;
        }

        // Clear active pointer if we just removed the active profile — we never
        // want leaf commands to resolve to a non-existent profile name.
        var global = await globalConfig.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        if (string.Equals(global.ActiveProfile, existing.Id, StringComparison.OrdinalIgnoreCase))
        {
            global.ActiveProfile = null;
            await globalConfig.SaveAsync(global, CancellationToken.None).ConfigureAwait(false);
            _logger.LogWarning("Active profile pointer cleared (was '{Id}'). Run 'txc config profile select <name>'.", existing.Id);
        }

        if (Cascade)
        {
            var remaining = await profileStore.ListAsync(CancellationToken.None).ConfigureAwait(false);

            var credStillUsed = remaining.Any(p =>
                string.Equals(p.CredentialRef, existing.CredentialRef, StringComparison.OrdinalIgnoreCase));
            if (!credStillUsed)
            {
                var cred = await credentialStore.GetAsync(existing.CredentialRef, CancellationToken.None).ConfigureAwait(false);
                if (cred is { SecretRef: { } secretRef })
                {
                    try { await vault.DeleteSecretAsync(secretRef, CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Credential '{Alias}' secret could not be removed from the vault.",
                            existing.CredentialRef);
                    }
                }
                await credentialStore.DeleteAsync(existing.CredentialRef, CancellationToken.None).ConfigureAwait(false);
                _logger.LogInformation("Credential '{Alias}' deleted (cascade).", existing.CredentialRef);
            }
            else
            {
                _logger.LogInformation(
                    "Credential '{Alias}' kept (still referenced by another profile).",
                    existing.CredentialRef);
            }

            var connStillUsed = remaining.Any(p =>
                string.Equals(p.ConnectionRef, existing.ConnectionRef, StringComparison.OrdinalIgnoreCase));
            if (!connStillUsed)
            {
                await connectionStore.DeleteAsync(existing.ConnectionRef, CancellationToken.None).ConfigureAwait(false);
                _logger.LogInformation("Connection '{Name}' deleted (cascade).", existing.ConnectionRef);
            }
            else
            {
                _logger.LogInformation(
                    "Connection '{Name}' kept (still referenced by another profile).",
                    existing.ConnectionRef);
            }
        }

        _logger.LogInformation("Profile '{Id}' deleted.", existing.Id);
        OutputFormatter.WriteResult("succeeded", $"Profile '{existing.Id}' deleted.");
        return ExitSuccess;
    }
}
