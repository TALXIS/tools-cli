using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Core.Platforms.PowerPlatform;

/// <summary>
/// Result of provisioning an Entra user into a Dataverse environment via
/// <see cref="IEnvironmentUserProvisioningService.ProvisionUserAsync"/>.
/// </summary>
public sealed record EnvironmentUserProvisionResult(
    Guid AadObjectId,
    string? UserPrincipalName,
    string? DisplayName);

/// <summary>
/// Provisions a brand-new Entra user into a Dataverse environment so they can
/// be assigned security roles immediately, without waiting for the user to
/// sign in once and be picked up by background JIT sync. Backs
/// <c>txc security user add --environment ...</c>.
/// </summary>
public interface IEnvironmentUserProvisioningService
{
    /// <summary>
    /// Resolves <paramref name="userIdOrUpn"/> (UPN or Entra object ID) via
    /// Microsoft Graph, then ensures a Dataverse <c>systemuser</c> record
    /// exists for that identity in the selected environment. Safe to call
    /// again for a user who already has access.
    /// </summary>
    Task<EnvironmentUserProvisionResult> ProvisionUserAsync(
        string? profileName,
        string userIdOrUpn,
        CancellationToken ct,
        Guid? environmentId = null);

    /// <summary>
    /// Applies the environment admin role to the current authenticated
    /// caller (<paramref name="connection"/>/<paramref name="credential"/>)
    /// in the given environment. Backs <c>txc security user self-elevate</c>.
    /// </summary>
    Task SelfElevateAsync(
        Connection connection,
        Credential credential,
        Guid environmentId,
        CancellationToken ct);
}
