using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.Model;

namespace TALXIS.CLI.Config.Storage;

public sealed class ConnectionStore : IConnectionStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ConnectionStore(ConfigPaths paths) { _path = paths.ConnectionsFile; }

    public async Task<IReadOnlyList<Connection>> ListAsync(CancellationToken ct)
    {
        var collection = await JsonFile.ReadOrDefaultAsync<ConnectionCollection>(_path, ct).ConfigureAwait(false);
        return collection.Connections;
    }

    public async Task<Connection?> GetAsync(string id, CancellationToken ct)
    {
        var collection = await JsonFile.ReadOrDefaultAsync<ConnectionCollection>(_path, ct).ConfigureAwait(false);
        return collection.Connections.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task UpsertAsync(Connection connection, CancellationToken ct)
    {
        if (connection is null) throw new ArgumentNullException(nameof(connection));
        if (string.IsNullOrWhiteSpace(connection.Id)) throw new ArgumentException("Connection.Id is required.", nameof(connection));

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var collection = await JsonFile.ReadOrDefaultAsync<ConnectionCollection>(_path, ct).ConfigureAwait(false);
            collection.Connections.RemoveAll(c => string.Equals(c.Id, connection.Id, StringComparison.OrdinalIgnoreCase));
            collection.Connections.Add(connection);
            await JsonFile.WriteAtomicAsync(_path, collection, ct).ConfigureAwait(false);
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var collection = await JsonFile.ReadOrDefaultAsync<ConnectionCollection>(_path, ct).ConfigureAwait(false);
            var removed = collection.Connections.RemoveAll(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
            if (removed == 0) return false;
            await JsonFile.WriteAtomicAsync(_path, collection, ct).ConfigureAwait(false);
            return true;
        }
        finally { _lock.Release(); }
    }
}
