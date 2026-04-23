using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Core.Storage;

public sealed class ProfileStore : IProfileStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ProfileStore(ConfigPaths paths) { _path = paths.ProfilesFile; }

    public async Task<IReadOnlyList<Profile>> ListAsync(CancellationToken ct)
    {
        var collection = await JsonFile.ReadOrDefaultAsync<ProfileCollection>(_path, ct).ConfigureAwait(false);
        return collection.Profiles;
    }

    public async Task<Profile?> GetAsync(string id, CancellationToken ct)
    {
        var collection = await JsonFile.ReadOrDefaultAsync<ProfileCollection>(_path, ct).ConfigureAwait(false);
        return collection.Profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task UpsertAsync(Profile profile, CancellationToken ct)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        if (string.IsNullOrWhiteSpace(profile.Id)) throw new ArgumentException("Profile.Id is required.", nameof(profile));

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var collection = await JsonFile.ReadOrDefaultAsync<ProfileCollection>(_path, ct).ConfigureAwait(false);
            collection.Profiles.RemoveAll(p => string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
            collection.Profiles.Add(profile);
            await JsonFile.WriteAtomicAsync(_path, collection, ct).ConfigureAwait(false);
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var collection = await JsonFile.ReadOrDefaultAsync<ProfileCollection>(_path, ct).ConfigureAwait(false);
            var removed = collection.Profiles.RemoveAll(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            if (removed == 0) return false;
            await JsonFile.WriteAtomicAsync(_path, collection, ct).ConfigureAwait(false);
            return true;
        }
        finally { _lock.Release(); }
    }
}
