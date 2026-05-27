namespace TALXIS.CLI.MCP;

internal static class McpPathNormalizer
{
    public static string NormalizeOperationalPath(string path, bool allowFileUriLocalPathHome = false)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must not be empty.", nameof(path));

        var expanded = ExpandDriveQualifiedHomePath(path);
        if (expanded != null)
            return Path.GetFullPath(expanded);

        return Path.GetFullPath(ExpandHomeRelativePath(path, allowFileUriLocalPathHome));
    }

    internal static string ExpandHomeRelativePath(string path, bool allowFileUriLocalPathHome = false)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        var suffixStart = GetHomeRelativeSuffixStart(path, allowFileUriLocalPathHome);
        if (suffixStart < 0)
            return path;

        return TryExpandHomePath(path[suffixStart..]) ?? path;
    }

    internal static string? ExpandDriveQualifiedHomePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var driveQualifiedHomeRemainder = TryGetDriveQualifiedHomeRemainder(path);
        if (driveQualifiedHomeRemainder != null)
            return TryExpandHomePath(driveQualifiedHomeRemainder);

        var fileUriDriveQualifiedHomeRemainder = TryGetFileUriDriveQualifiedHomeRemainder(path);
        if (fileUriDriveQualifiedHomeRemainder != null)
            return TryExpandHomePath(fileUriDriveQualifiedHomeRemainder);

        return TryNormalizeWindowsFileUriDrivePath(path);
    }

    private static string? TryExpandHomePath(string remainder)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
            return null;

        var trimmedRemainder = remainder.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '\\', '/');
        if (string.IsNullOrEmpty(trimmedRemainder))
            return home;

        var segments = trimmedRemainder.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 ? home : Path.Combine([home, .. segments]);
    }

    // Matches drive-qualified home paths like C:/~/project and C:\~\project.
    private static string? TryGetDriveQualifiedHomeRemainder(string path)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        if (!IsDriveQualifiedPath(path))
            return null;

        if (path[2] == '~')
            return path.Length == 3
                ? string.Empty
                : IsDirectorySeparator(path[3])
                    ? path[4..]
                    : null;

        if (path.Length >= 4 && IsDirectorySeparator(path[2]) && path[3] == '~')
            return path.Length == 4
                ? string.Empty
                : IsDirectorySeparator(path[4])
                    ? path[4..]
                    : null;

        return null;
    }

    // Matches file URI local paths like /C:/~/project after Uri.LocalPath.
    private static string? TryGetFileUriDriveQualifiedHomeRemainder(string path)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        if (path.Length < 5 || path[0] != '/' || !char.IsLetter(path[1]) || path[2] != ':' || !IsDirectorySeparator(path[3]) || path[4] != '~')
            return null;

        return path.Length == 5
            ? string.Empty
            : IsDirectorySeparator(path[5])
                ? path[5..]
                : null;
    }

    // Drops the leading slash from file URI local paths like /c:/project.
    private static string? TryNormalizeWindowsFileUriDrivePath(string path)
    {
        if (OperatingSystem.IsWindows() && path.Length >= 3
            && path[0] == '/' && char.IsLetter(path[1]) && path[2] == ':')
        {
            return path[1..];
        }

        return null;
    }

    private static bool IsDriveQualifiedPath(string path)
    {
        return path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':';
    }

    private static int GetHomeRelativeSuffixStart(string path, bool allowFileUriLocalPathHome)
    {
        if (path == "~")
            return 1;

        if (path.Length >= 2 && path[0] == '~' && IsDirectorySeparator(path[1]))
            return 2;

        if (!allowFileUriLocalPathHome)
            return -1;

        if (path.Length == 2 && IsDirectorySeparator(path[0]) && path[1] == '~')
            return 2;

        // Uri.LocalPath for file:///~/project retains a leading separator, so
        // accept /~/... here without changing ordinary absolute paths.
        if (path.Length >= 3 && IsDirectorySeparator(path[0]) && path[1] == '~' && IsDirectorySeparator(path[2]))
            return 3;

        return -1;
    }

    private static bool IsDirectorySeparator(char value)
        => value == Path.DirectorySeparatorChar
        || value == Path.AltDirectorySeparatorChar
        || value is '\\' or '/';
}
