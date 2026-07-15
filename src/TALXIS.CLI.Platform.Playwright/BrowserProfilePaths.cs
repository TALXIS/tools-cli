using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Storage;

namespace TALXIS.CLI.Platform.Playwright;

internal static class BrowserProfilePaths
{
    public static string BrowserRoot(ConfigPaths paths)
        => Path.Combine(paths.Root, "browser");

    public static string ProfileDirectory(ConfigPaths paths, string profileName)
        => Path.Combine(BrowserRoot(paths), SanitizeProfileName(profileName));

    public static string UserDataDirectory(ConfigPaths paths, string profileName)
        => Path.Combine(ProfileDirectory(paths, profileName), "user-data");

    public static string SessionFile(ConfigPaths paths, string profileName)
        => Path.Combine(ProfileDirectory(paths, profileName), "session.json");

    public static string StorageStateFile(ConfigPaths paths, string profileName)
        => Path.Combine(ProfileDirectory(paths, profileName), "storage-state.enc");

    public static SecretRef StorageStateKeyRef(string profileName)
        => SecretRef.Create($"browser-{SanitizeProfileName(profileName)}", "storage-state-key");

    public static void EnsureProfileDirectories(ConfigPaths paths, string profileName)
    {
        Directory.CreateDirectory(ProfileDirectory(paths, profileName));
        Directory.CreateDirectory(UserDataDirectory(paths, profileName));
    }

    private static string SanitizeProfileName(string profileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(profileName.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "default" : sanitized;
    }
}
