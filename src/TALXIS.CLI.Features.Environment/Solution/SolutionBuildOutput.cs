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

    // Older Build SDKs pack an incomplete zip and exit 0 when a required component is missing; the
    // only trace is this packager line, printed without any Error: prefix. Unlike the root-components
    // case below, there is no known false positive for it, so any occurrence is still an error.
    [GeneratedRegex(@"^\s*Following objects, required by the solution, are not present", RegexOptions.IgnoreCase)]
    private static partial Regex MissingObjectsPattern();

    // Same older-SDK trace, but for root components. The header line alone carries no component
    // info - it's followed by one "Type='...', Id (or schema name)='...'." line per component,
    // matched below - so by itself it's only a hint, not conclusive.
    [GeneratedRegex(@"^\s*Following root components are not defined in customizations", RegexOptions.IgnoreCase)]
    private static partial Regex MissingRootComponentsHeaderPattern();

    // tools-devkit-build#114: SolutionPackagerLib's root-component check reports connectors
    // (component type 372, "ECConnector" in its own enum) as missing unconditionally, even when
    // correctly packed - the Build SDK's own InvokeSolutionPackager task already independently
    // confirms the connector was actually staged before letting such a build succeed, so a genuinely
    // missing connector still fails there with a non-zero exit code. It's therefore safe for this
    // text scan to stop treating the line as an error for that type specifically.
    [GeneratedRegex(@"Type='([^']+)',\s*Id \(or schema name\)='([^']+)'", RegexOptions.IgnoreCase)]
    private static partial Regex MissingRootComponentDetailPattern();

    private static readonly HashSet<string> KnownFalsePositiveRootComponentTypes =
        new(StringComparer.OrdinalIgnoreCase) { "ECConnector" };

    [GeneratedRegex(@"\bwarning(\s+[A-Za-z]+\d+)?\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex WarningPattern();

    internal static BuildOutputSeverity Classify(string line)
    {
        if (ErrorPattern().IsMatch(line)) return BuildOutputSeverity.Error;
        if (MissingObjectsPattern().IsMatch(line)) return BuildOutputSeverity.Error;

        var detail = MissingRootComponentDetailPattern().Match(line);
        if (detail.Success)
        {
            return KnownFalsePositiveRootComponentTypes.Contains(detail.Groups[1].Value)
                ? BuildOutputSeverity.Warning
                : BuildOutputSeverity.Error;
        }

        if (MissingRootComponentsHeaderPattern().IsMatch(line)) return BuildOutputSeverity.Warning;
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
