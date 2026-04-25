namespace TALXIS.CLI.Core.Model;

/// <summary>
/// Canonical credential kinds supported by txc v1. JSON serializes these as
/// kebab-case via <see cref="Storage.TxcJsonOptions"/>.
/// </summary>
public enum CredentialKind
{
    InteractiveBrowser = 0,
    DeviceCode = 1,
    ClientSecret = 2,
    ClientCertificate = 3,
    ManagedIdentity = 4,
    WorkloadIdentityFederation = 5,
    AzureCli = 6,
    Pat = 7,
}
