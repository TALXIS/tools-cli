using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.Model;

namespace TALXIS.CLI.Config.Storage;

public sealed class CredentialStore : ICredentialStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public CredentialStore(ConfigPaths paths) { _path = paths.CredentialsFile; }

    public async Task<IReadOnlyList<Credential>> ListAsync(CancellationToken ct)
    {
        var collection = await JsonFile.ReadOrDefaultAsync<CredentialCollection>(_path, ct).ConfigureAwait(false);
        return collection.Credentials;
    }

    public async Task<Credential?> GetAsync(string id, CancellationToken ct)
    {
        var collection = await JsonFile.ReadOrDefaultAsync<CredentialCollection>(_path, ct).ConfigureAwait(false);
        return collection.Credentials.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task UpsertAsync(Credential credential, CancellationToken ct)
    {
        if (credential is null) throw new ArgumentNullException(nameof(credential));
        if (string.IsNullOrWhiteSpace(credential.Id)) throw new ArgumentException("Credential.Id is required.", nameof(credential));

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var collection = await JsonFile.ReadOrDefaultAsync<CredentialCollection>(_path, ct).ConfigureAwait(false);
            collection.Credentials.RemoveAll(c => string.Equals(c.Id, credential.Id, StringComparison.OrdinalIgnoreCase));
            collection.Credentials.Add(credential);
            await JsonFile.WriteAtomicAsync(_path, collection, ct).ConfigureAwait(false);
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var collection = await JsonFile.ReadOrDefaultAsync<CredentialCollection>(_path, ct).ConfigureAwait(false);
            var removed = collection.Credentials.RemoveAll(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
            if (removed == 0) return false;
            await JsonFile.WriteAtomicAsync(_path, collection, ct).ConfigureAwait(false);
            return true;
        }
        finally { _lock.Release(); }
    }
}
