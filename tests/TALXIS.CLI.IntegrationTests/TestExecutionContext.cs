using System;
using System.IO;

namespace TALXIS.CLI.IntegrationTests;

internal static class TestExecutionContext
{
    private static readonly Lazy<string> _buildConfiguration = new(ResolveBuildConfiguration);
    private static readonly Lazy<string> _repositoryRoot = new(ResolveRepositoryRoot);

    public static string BuildConfiguration => _buildConfiguration.Value;

    public static string RepositoryRoot => _repositoryRoot.Value;

    public static string GetProjectPath(params string[] relativePathSegments)
    {
        var path = RepositoryRoot;

        foreach (var segment in relativePathSegments)
            path = Path.Combine(path, segment);

        return path;
    }

    private static string ResolveBuildConfiguration()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            if (string.Equals(directory.Parent?.Name, "bin", StringComparison.OrdinalIgnoreCase))
                return directory.Name;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not determine test build configuration from AppContext.BaseDirectory");
    }

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "TALXIS.CLI.sln")))
            directory = directory.Parent;

        if (directory == null)
            throw new InvalidOperationException("Could not find repository root");

        return directory.FullName;
    }
}
