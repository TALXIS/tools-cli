using TALXIS.CLI.Core.Abstractions;
using ConnectionModel = TALXIS.CLI.Core.Model.Connection;

namespace TALXIS.CLI.Tests.Config.Commands;

/// <summary>
/// Connection store decorator whose writes can be switched to fail, to prove
/// callers do not act on state that was never persisted.
/// </summary>
internal sealed class FailingConnectionStore(IConnectionStore inner) : IConnectionStore
{
    public bool FailUpserts { get; set; }

    public Task<IReadOnlyList<ConnectionModel>> ListAsync(CancellationToken ct) => inner.ListAsync(ct);

    public Task<ConnectionModel?> GetAsync(string id, CancellationToken ct) => inner.GetAsync(id, ct);

    public Task UpsertAsync(ConnectionModel connection, CancellationToken ct)
        => FailUpserts ? throw new IOException("config directory is not writable") : inner.UpsertAsync(connection, ct);

    public Task<bool> DeleteAsync(string id, CancellationToken ct) => inner.DeleteAsync(id, ct);
}
