using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.DependencyInjection;
using TALXIS.CLI.Config.Model;
using TALXIS.CLI.Dataverse;

namespace TALXIS.CLI.Config.Providers.Dataverse.Runtime;

/// <summary>
/// Shared helper that every refactored Dataverse leaf command uses to turn
/// a <c>--profile</c> string (or null => resolver defaults / TXC_PROFILE)
/// into a ready-to-use <see cref="DataverseConnection"/>.
/// </summary>
public static class DataverseCommandBridge
{
    public static async Task<DataverseConnection> ConnectAsync(string? profileName, CancellationToken ct)
    {
        var resolver = TxcServices.Get<IConfigurationResolver>();
        var factory = TxcServices.Get<IDataverseConnectionFactory>();
        var context = await resolver.ResolveAsync(profileName, ct).ConfigureAwait(false);
        return await factory.ConnectAsync(context, ct).ConfigureAwait(false);
    }

    public static async Task<ResolvedProfileContext> ResolveAsync(string? profileName, CancellationToken ct)
    {
        var resolver = TxcServices.Get<IConfigurationResolver>();
        return await resolver.ResolveAsync(profileName, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Transitional helper for subprocess-based commands (PackageImport,
    /// DataPackageImport) that still expect a Dataverse connection string.
    /// Supports only <see cref="CredentialKind.ClientSecret"/> in v1 —
    /// this is the single credential kind that can be safely inlined into
    /// a connection string for the child process. Every other kind
    /// (including <see cref="CredentialKind.ClientCertificate"/>,
    /// <see cref="CredentialKind.InteractiveBrowser"/>, federation, etc.)
    /// is rejected with a deterministic message until the subprocess gets
    /// its own MSAL cache access via TXC_CONFIG_DIR.
    /// TODO: add certificate + MSAL-cache paths in a follow-up milestone.
    /// </summary>
    public static async Task<string> BuildConnectionStringAsync(string? profileName, CancellationToken ct)
    {
        var context = await ResolveAsync(profileName, ct).ConfigureAwait(false);
        if (context.Connection.Provider != ProviderKind.Dataverse)
            throw new InvalidOperationException(
                $"Connection '{context.Connection.Id}' has provider {context.Connection.Provider}, expected {ProviderKind.Dataverse}.");
        if (string.IsNullOrWhiteSpace(context.Connection.EnvironmentUrl))
            throw new InvalidOperationException($"Dataverse connection '{context.Connection.Id}' is missing EnvironmentUrl.");

        var url = context.Connection.EnvironmentUrl!.TrimEnd('/');
        var cred = context.Credential;

        switch (cred.Kind)
        {
            case CredentialKind.ClientSecret:
                {
                    if (string.IsNullOrWhiteSpace(cred.ApplicationId))
                        throw new InvalidOperationException($"Credential '{cred.Id}' is missing ApplicationId.");
                    if (cred.SecretRef is null)
                        throw new InvalidOperationException($"Credential '{cred.Id}' has no SecretRef for its client secret.");
                    var vault = TxcServices.Get<ICredentialVault>();
                    var secret = await vault.GetSecretAsync(cred.SecretRef, ct).ConfigureAwait(false)
                        ?? throw new InvalidOperationException($"Vault could not return a client secret for credential '{cred.Id}'.");
                    return $"AuthType=ClientSecret;Url={url};ClientId={cred.ApplicationId};ClientSecret={secret}";
                }
            case CredentialKind.InteractiveBrowser:
            case CredentialKind.DeviceCode:
            case CredentialKind.WorkloadIdentityFederation:
            case CredentialKind.ManagedIdentity:
            case CredentialKind.AzureCli:
            case CredentialKind.ClientCertificate:
            case CredentialKind.Pat:
            default:
                throw new NotSupportedException(
                    $"Credential kind '{cred.Kind}' cannot currently be used with subprocess-based commands (package import / data package import). " +
                    "Use a client-secret credential for now. Full support for interactive and federated credentials lands with the package-deployer-subprocess milestone.");
        }
    }
}

