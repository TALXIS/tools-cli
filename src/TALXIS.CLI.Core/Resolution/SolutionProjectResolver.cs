using System.Xml.Linq;

namespace TALXIS.CLI.Core.Resolution;

/// <summary>
/// Resolves Dataverse solution project conventions: finds <c>.cdsproj</c> / <c>.csproj</c>
/// files, reads the <c>SolutionRootPath</c> MSBuild property, and locates
/// <c>Other/Solution.xml</c> to extract the solution unique name.
/// </summary>
/// <remarks>
/// Multiple commands need these conventions (import, export, url open, data model convert).
/// Centralizing them here avoids duplication and ensures consistent behavior.
/// </remarks>
public static class SolutionProjectResolver
{
    /// <summary>Default fallback when <c>SolutionRootPath</c> is not declared in the project file.</summary>
    public const string DefaultSolutionRootPath = "src";

    /// <summary>
    /// Finds the first <c>.cdsproj</c> or <c>.csproj</c> in the given directory.
    /// <c>.cdsproj</c> is preferred (Dataverse convention); <c>.csproj</c> is the fallback.
    /// Returns null if no project file is found.
    /// </summary>
    public static string? FindProjectFile(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
            return null;

        return Directory.EnumerateFiles(directoryPath, "*.cdsproj").FirstOrDefault()
            ?? Directory.EnumerateFiles(directoryPath, "*.csproj").FirstOrDefault();
    }

    /// <summary>
    /// Reads the <c>SolutionRootPath</c> MSBuild property from a project file.
    /// Returns the raw property value, or null if the property is not defined.
    /// </summary>
    public static string? ReadSolutionRootPath(string projectFilePath)
    {
        var doc = XDocument.Load(projectFilePath);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        return doc.Descendants(ns + "SolutionRootPath").FirstOrDefault()?.Value;
    }

    /// <summary>
    /// Resolves the solution root directory from a project file.
    /// Reads <c>SolutionRootPath</c> from the project, falls back to <paramref name="fallback"/>,
    /// and returns the absolute path. Returns null if the resolved directory does not exist.
    /// </summary>
    public static string? ResolveSolutionRoot(string projectFilePath, string fallback = DefaultSolutionRootPath)
    {
        var projectDir = Path.GetDirectoryName(projectFilePath)!;
        var solutionRootPath = ReadSolutionRootPath(projectFilePath);

        if (string.IsNullOrWhiteSpace(solutionRootPath))
            solutionRootPath = fallback;

        var resolved = Path.GetFullPath(Path.Combine(projectDir, solutionRootPath));
        return Directory.Exists(resolved) ? resolved : null;
    }

    /// <summary>
    /// Reads the <c>&lt;UniqueName&gt;</c> from <c>Other/Solution.xml</c> under the given solution root.
    /// Returns null if the file does not exist or the element is missing.
    /// </summary>
    public static string? ReadSolutionUniqueName(string solutionRootPath)
    {
        var solutionXmlPath = Path.Combine(solutionRootPath, "Other", "Solution.xml");
        if (!File.Exists(solutionXmlPath))
            return null;

        var doc = XDocument.Load(solutionXmlPath);
        return doc.Descendants("UniqueName").FirstOrDefault()?.Value;
    }

    /// <summary>
    /// Walks up from a file path to find the solution workspace root —
    /// the directory containing <c>Other/Solution.xml</c>. Also checks for
    /// <c>.cdsproj</c> / <c>.csproj</c> files that declare a <c>SolutionRootPath</c>.
    /// Returns null if no workspace root can be found.
    /// </summary>
    public static string? FindWorkspaceRoot(string startPath)
    {
        var dir = File.Exists(startPath) ? Path.GetDirectoryName(startPath) : startPath;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Other", "Solution.xml")))
                return dir;

            var projectFile = FindProjectFile(dir);
            if (projectFile is not null)
            {
                var solutionRootPath = ReadSolutionRootPath(projectFile) ?? ".";
                var candidateRoot = Path.GetFullPath(Path.Combine(dir, solutionRootPath));
                if (File.Exists(Path.Combine(candidateRoot, "Other", "Solution.xml")))
                    return candidateRoot;
            }
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
