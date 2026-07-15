using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Storage;

namespace TALXIS.CLI.Platform.Playwright;

public sealed class StorageStateManager
{
    private readonly ConfigPaths _paths;
    private readonly ICredentialVault _vault;

    public StorageStateManager(ConfigPaths paths, ICredentialVault vault)
    {
        _paths = paths;
        _vault = vault;
    }

    public async Task SaveAsync(IBrowserContext context, string profileName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        var json = await context.StorageStateAsync().ConfigureAwait(false);
        var key = await GetOrCreateKeyAsync(profileName, ct).ConfigureAwait(false);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plainBytes = Encoding.UTF8.GetBytes(json);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[16];

        using (var aes = new AesGcm(key, 16))
        {
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag);
        }

        var envelope = new StorageStateEnvelope
        {
            Nonce = Convert.ToBase64String(nonce),
            CipherText = Convert.ToBase64String(cipherBytes),
            Tag = Convert.ToBase64String(tag),
        };

        var path = BrowserProfilePaths.StorageStateFile(_paths, profileName);
        await JsonFile.WriteAtomicAsync(path, envelope, ct).ConfigureAwait(false);
    }

    public async Task<string?> LoadAsync(string profileName, CancellationToken ct)
    {
        var path = BrowserProfilePaths.StorageStateFile(_paths, profileName);
        if (!File.Exists(path))
            return null;

        var key = await GetOrCreateKeyAsync(profileName, ct).ConfigureAwait(false);
        var envelope = await JsonFile.ReadOrDefaultAsync<StorageStateEnvelope>(path, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(envelope.Nonce)
            || string.IsNullOrWhiteSpace(envelope.CipherText)
            || string.IsNullOrWhiteSpace(envelope.Tag))
        {
            return null;
        }

        var nonce = Convert.FromBase64String(envelope.Nonce);
        var cipherBytes = Convert.FromBase64String(envelope.CipherText);
        var tag = Convert.FromBase64String(envelope.Tag);
        var plainBytes = new byte[cipherBytes.Length];

        using (var aes = new AesGcm(key, 16))
        {
            aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
        }

        return Encoding.UTF8.GetString(plainBytes);
    }

    public Task<bool> ExistsAsync(string profileName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(BrowserProfilePaths.StorageStateFile(_paths, profileName)));
    }

    public async Task DeleteAsync(string profileName, CancellationToken ct)
    {
        var path = BrowserProfilePaths.StorageStateFile(_paths, profileName);
        if (File.Exists(path))
            File.Delete(path);

        await _vault.DeleteSecretAsync(BrowserProfilePaths.StorageStateKeyRef(profileName), ct).ConfigureAwait(false);
    }

    private async Task<byte[]> GetOrCreateKeyAsync(string profileName, CancellationToken ct)
    {
        var secretRef = BrowserProfilePaths.StorageStateKeyRef(profileName);
        var current = await _vault.GetSecretAsync(secretRef, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(current))
            return Convert.FromBase64String(current);

        var key = RandomNumberGenerator.GetBytes(32);
        await _vault.SetSecretAsync(secretRef, Convert.ToBase64String(key), ct).ConfigureAwait(false);
        return key;
    }

    private sealed class StorageStateEnvelope
    {
        public string? Nonce { get; set; }
        public string? CipherText { get; set; }
        public string? Tag { get; set; }
    }
}
