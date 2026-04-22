using TALXIS.CLI.Config.Model;

namespace TALXIS.CLI.Config.Abstractions;

public interface IProfileStore
{
    Task<IReadOnlyList<Profile>> ListAsync(CancellationToken ct);
    Task<Profile?> GetAsync(string id, CancellationToken ct);
    Task UpsertAsync(Profile profile, CancellationToken ct);
    Task<bool> DeleteAsync(string id, CancellationToken ct);
}

public interface IConnectionStore
{
    Task<IReadOnlyList<Connection>> ListAsync(CancellationToken ct);
    Task<Connection?> GetAsync(string id, CancellationToken ct);
    Task UpsertAsync(Connection connection, CancellationToken ct);
    Task<bool> DeleteAsync(string id, CancellationToken ct);
}

public interface ICredentialStore
{
    Task<IReadOnlyList<Credential>> ListAsync(CancellationToken ct);
    Task<Credential?> GetAsync(string id, CancellationToken ct);
    Task UpsertAsync(Credential credential, CancellationToken ct);
    Task<bool> DeleteAsync(string id, CancellationToken ct);
}

public interface IGlobalConfigStore
{
    Task<GlobalConfig> LoadAsync(CancellationToken ct);
    Task SaveAsync(GlobalConfig config, CancellationToken ct);
}
