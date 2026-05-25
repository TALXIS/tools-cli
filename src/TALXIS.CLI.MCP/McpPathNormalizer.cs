namespace TALXIS.CLI.MCP;

internal static class McpPathNormalizer
{
    public static string NormalizeOperationalPath(string path, bool allowFileUriLocalPathHome = false)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must not be empty.", nameof(path));

        return Path.GetFullPath(ExpandHomeRelativePath(path, allowFileUriLocalPathHome));
    }

    internal static string ExpandHomeRelativePath(string path, bool allowFileUriLocalPathHome = false)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        var suffixStart = GetHomeRelativeSuffixStart(path, allowFileUriLocalPathHome);
        if (suffixStart < 0)
            return path;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
            return path;

        var remainder = path[suffixStart..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '\\', '/');
        if (string.IsNullOrEmpty(remainder))
            return home;

        var segments = remainder.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 ? home : Path.Combine([home, .. segments]);
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
