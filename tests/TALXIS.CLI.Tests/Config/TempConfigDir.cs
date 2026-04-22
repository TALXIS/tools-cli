using TALXIS.CLI.Config.Storage;

namespace TALXIS.CLI.Tests.Config;

internal sealed class TempConfigDir : IDisposable
{
    public string Path { get; }
    public ConfigPaths Paths { get; }

    public TempConfigDir()
    {
        Path = Directory.CreateTempSubdirectory("txc-test-").FullName;
        Paths = new ConfigPaths(Path);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
    }
}
