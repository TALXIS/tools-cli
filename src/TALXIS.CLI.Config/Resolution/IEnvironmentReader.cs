namespace TALXIS.CLI.Config.Resolution;

/// <summary>
/// Thin abstraction over <see cref="System.Environment"/> so tests can inject
/// a deterministic environment without touching global process state.
/// </summary>
public interface IEnvironmentReader
{
    string? Get(string name);
    string GetCurrentDirectory();
}

public sealed class ProcessEnvironmentReader : IEnvironmentReader
{
    public static readonly ProcessEnvironmentReader Instance = new();
    public string? Get(string name) => Environment.GetEnvironmentVariable(name);
    public string GetCurrentDirectory() => Directory.GetCurrentDirectory();
}
