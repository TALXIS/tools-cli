namespace TALXIS.CLI.Config.Model;

/// <summary>
/// Canonical credential kinds supported by txc v1. JSON serializes these as
/// kebab-case via <see cref="Storage.TxcJsonOptions"/>.
/// </summary>
public enum CredentialKind
{
    InteractiveBrowser,
    DeviceCode,
    ClientSecret,
    ClientCertificate,
    ManagedIdentity,
    WorkloadIdentityFederation,
    AzureCli,
    Pat,
}
