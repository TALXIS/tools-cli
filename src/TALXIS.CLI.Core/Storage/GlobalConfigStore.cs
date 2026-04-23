using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Core.Storage;

public sealed class GlobalConfigStore : IGlobalConfigStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public GlobalConfigStore(ConfigPaths paths) { _path = paths.GlobalConfigFile; }

    public Task<GlobalConfig> LoadAsync(CancellationToken ct)
        => JsonFile.ReadOrDefaultAsync<GlobalConfig>(_path, ct);

    public async Task SaveAsync(GlobalConfig config, CancellationToken ct)
    {
        if (config is null) throw new ArgumentNullException(nameof(config));
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try { await JsonFile.WriteAtomicAsync(_path, config, ct).ConfigureAwait(false); }
        finally { _lock.Release(); }
    }
}
