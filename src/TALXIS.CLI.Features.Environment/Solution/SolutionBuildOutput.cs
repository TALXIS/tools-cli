using System.Text.Json;
using System.Text.RegularExpressions;

namespace TALXIS.CLI.Features.Environment.Solution;

internal enum BuildOutputSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// Helpers for interpreting <c>dotnet build</c> output and locating the solution ZIP it produces.
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
    /// Reads <c>OutputPath</c> from the JSON that <c>dotnet build -getProperty:OutputPath</c> prints.
    /// Returns null when the output is not that JSON or the property is empty.
    /// </summary>
    internal static string? ParseOutputPath(string getPropertyOutput)
    {
        var start = getPropertyOutput.IndexOf('{');
        var end = getPropertyOutput.LastIndexOf('}');
        if (start < 0 || end < start) return null;

        try
        {
            using var doc = JsonDocument.Parse(getPropertyOutput[start..(end + 1)]);
            if (!doc.RootElement.TryGetProperty("Properties", out var props)
                || !props.TryGetProperty("OutputPath", out var value)
                || value.ValueKind != JsonValueKind.String)
                return null;

            var path = value.GetString();
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The path the Build SDK packs the solution to: <c>$(OutputPath)$(MSBuildProjectName).zip</c>.
    /// MSBuild reports <c>OutputPath</c> relative to the project directory, often with mixed
    /// separators (<c>bin\Release/net472/</c>), whatever the OS.
    /// </summary>
    internal static string ResolveZipPath(string csProjPath, string outputPath)
    {
        var projectDir = Path.GetDirectoryName(Path.GetFullPath(csProjPath))!;
        var normalized = outputPath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(projectDir, normalized, Path.GetFileNameWithoutExtension(csProjPath) + ".zip"));
    }
}
