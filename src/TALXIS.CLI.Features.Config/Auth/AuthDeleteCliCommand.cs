using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Config.Auth;

/// <summary>
/// <c>txc config auth delete &lt;alias&gt;</c> — removes the credential
/// entry from the store and deletes any associated secret from the OS
/// vault.
///
/// Profiles that reference the deleted credential are <b>left in place
/// but orphaned</b>; the command warns about each one. pac CLI's
/// <c>pac auth clear</c> behaves similarly; cascading deletes would be
/// surprising and are therefore intentionally not performed.
/// </summary>
[McpIgnore]
[CliCommand(
    Name = "delete",
    Description = "Delete a stored credential. Profiles referencing it are left orphaned with a warning."
)]
public class AuthDeleteCliCommand : TxcLeafCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(AuthDeleteCliCommand));
    protected override ILogger Logger => _logger;

    [CliArgument(Description = "Credential alias (id) to delete.")]
    public required string Alias { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(Alias))
        {
            _logger.LogError("Credential alias must be provided.");
            return ExitError;
        }

        var credStore = TxcServices.Get<ICredentialStore>();
        var profileStore = TxcServices.Get<IProfileStore>();
        var vault = TxcServices.Get<ICredentialVault>();

        var existing = await credStore.GetAsync(Alias, CancellationToken.None).ConfigureAwait(false);
        if (existing is null)
        {
            _logger.LogError("Credential '{Alias}' not found.", Alias);
            return ExitValidationError;
        }

        // Surface any profile that would be orphaned. Iterate profiles first
        // so the user sees the warning even if the vault delete below fails.
        var profiles = await profileStore.ListAsync(CancellationToken.None).ConfigureAwait(false);
        foreach (var p in profiles.Where(p =>
            string.Equals(p.CredentialRef, Alias, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning(
                "Profile '{ProfileId}' references credential '{Alias}' and will be orphaned. " +
                "Update or delete the profile explicitly.",
                p.Id, Alias);
        }

        // Best-effort secret purge — absent secret (interactive / WIF) is fine.
        if (existing.SecretRef is { } secretRef)
        {
            try
            {
                await vault.DeleteSecretAsync(secretRef, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Credential '{Alias}' secret could not be removed from the vault. " +
                    "You may need to delete it manually.",
                    Alias);
            }
        }

        var removed = await credStore.DeleteAsync(Alias, CancellationToken.None).ConfigureAwait(false);
        if (!removed)
        {
            _logger.LogError("Credential '{Alias}' disappeared during delete.", Alias);
            return ExitError;
        }

        _logger.LogInformation("Credential '{Alias}' deleted.", Alias);
        OutputFormatter.WriteResult("succeeded", $"Credential '{Alias}' deleted.");
        return ExitSuccess;
    }
}
