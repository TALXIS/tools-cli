using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Core.Abstractions;

/// <summary>
/// OS-level secret vault (DPAPI / Keychain / libsecret) holding material
/// referenced by <see cref="SecretRef"/>.
/// </summary>
public interface ICredentialVault
{
    Task<string?> GetSecretAsync(SecretRef reference, CancellationToken ct);
    Task SetSecretAsync(SecretRef reference, string value, CancellationToken ct);
    Task<bool> DeleteSecretAsync(SecretRef reference, CancellationToken ct);
    Task ClearAsync(CancellationToken ct);
}
