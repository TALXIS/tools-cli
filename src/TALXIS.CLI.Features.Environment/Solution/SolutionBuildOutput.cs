using System.Text.RegularExpressions;

namespace TALXIS.CLI.Features.Environment.Solution;

internal enum BuildOutputSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// Helpers for interpreting <c>dotnet build</c> output and locating the solution ZIP it produced.
/// </summary>
internal static partial class SolutionBuildOutput
{
    [GeneratedRegex(@"\berror(\s+[A-Za-z]+\d+)?\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex ErrorPattern();

    // Older Build SDKs pack an incomplete zip and exit 0 when root components have no source
    // files; the only trace is one of these packager lines, printed without any Error: prefix.
    [GeneratedRegex(@"^\s*(Following root components are not defined in customizations|Following objects, required by the solution, are not present)", RegexOptions.IgnoreCase)]
    private static partial Regex MissingRootComponentsPattern();

    [GeneratedRegex(@"\bwarning(\s+[A-Za-z]+\d+)?\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex WarningPattern();

    internal static BuildOutputSeverity Classify(string line)
    {
        if (ErrorPattern().IsMatch(line)) return BuildOutputSeverity.Error;
        if (MissingRootComponentsPattern().IsMatch(line)) return BuildOutputSeverity.Error;
        if (WarningPattern().IsMatch(line)) return BuildOutputSeverity.Warning;

        return BuildOutputSeverity.Info;
    }

    /// <summary>
    /// Finds solution ZIPs under the build output directory (any target framework subfolder).
    /// Fresh ZIPs are those written at or after <paramref name="buildStartUtc"/>, newest first.
    /// </summary>
    internal static (string[] Fresh, string[] All) FindSolutionZips(string binConfigDir, DateTime buildStartUtc)
    {
        if (!Directory.Exists(binConfigDir)) return (Array.Empty<string>(), Array.Empty<string>());

        var all = Directory.GetFiles(binConfigDir, "*.zip", SearchOption.AllDirectories);
        var fresh = all
            .Where(f => File.GetLastWriteTimeUtc(f) >= buildStartUtc)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();

        return (fresh, all);
    }
}
