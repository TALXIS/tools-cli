namespace TALXIS.CLI.Config.Storage;

/// <summary>
/// Resolves the effective configuration directory for this process.
/// Precedence: <c>TXC_CONFIG_DIR</c> environment variable, else
/// <c>$HOME/.txc</c> (<c>%USERPROFILE%\.txc</c> on Windows).
/// Paths are created lazily by stores when they first write.
/// </summary>
public sealed class ConfigPaths
{
    public const string EnvVar = "TXC_CONFIG_DIR";

    public string Root { get; }

    public string GlobalConfigFile => Path.Combine(Root, "config.json");
    public string ProfilesFile => Path.Combine(Root, "profiles.json");
    public string ConnectionsFile => Path.Combine(Root, "connections.json");
    public string CredentialsFile => Path.Combine(Root, "credentials.json");
    public string AuthDirectory => Path.Combine(Root, "auth");

    public ConfigPaths(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("Config root must not be empty.", nameof(root));
        Root = Path.GetFullPath(root);
    }

    public static ConfigPaths FromEnvironment(IDictionary<string, string?>? envOverride = null)
    {
        string? explicitDir = envOverride is null
            ? Environment.GetEnvironmentVariable(EnvVar)
            : (envOverride.TryGetValue(EnvVar, out var v) ? v : null);

        if (!string.IsNullOrWhiteSpace(explicitDir))
            return new ConfigPaths(explicitDir);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
            throw new InvalidOperationException("Unable to determine user profile directory for default TXC config path.");

        return new ConfigPaths(Path.Combine(home, ".txc"));
    }
}
