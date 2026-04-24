using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Platform.Dataverse.Runtime;

/// <summary>
/// Acquires Microsoft Entra access tokens for Dataverse given a resolved
/// (Connection, Credential) pair. This is the single place MSAL is driven
/// for the refactored Dataverse commands and the live <c>WhoAmI</c> check.
/// </summary>
/// <remarks>
/// Extends <see cref="IAccessTokenService"/> so the Dataverse implementation
/// can be consumed wherever a provider-agnostic token source is needed
/// (e.g. the Power Platform control-plane catalog).
/// <para>
/// Token-cache behaviour depends on the credential kind:
/// </para>
/// <list type="bullet">
///   <item><see cref="CredentialKind.InteractiveBrowser"/> — silent via the
///     shared MSAL user cache; throws a precise error if the user has to
///     re-run <c>txc config auth login</c>.</item>
///   <item><see cref="CredentialKind.ClientSecret"/> and
///     <see cref="CredentialKind.ClientCertificate"/> — confidential client,
///     MSAL's in-memory app-token cache (tokens are short-lived and
///     service-principal secrets rotate often, so we don't persist them).</item>
///   <item><see cref="CredentialKind.WorkloadIdentityFederation"/> — fresh
///     assertion per call via <c>FederatedAssertionCallbacks.AutoSelect</c>;
///     MSAL caches the resulting access token in-memory for its lifetime.</item>
/// </list>
/// <para>
/// Kinds not yet supported (DeviceCode, ManagedIdentity, AzureCli, Pat)
/// throw <see cref="NotSupportedException"/> with a remedy message.
/// </para>
/// </remarks>
public interface IDataverseAccessTokenService : IAccessTokenService
{
    /// <summary>
    /// Acquires a bearer token scoped to the Dataverse environment URL on
    /// <paramref name="connection"/> using the given
    /// <paramref name="credential"/>.
    /// </summary>
    Task<string> AcquireAsync(TALXIS.CLI.Core.Model.Connection connection, Credential credential, CancellationToken ct);
}
