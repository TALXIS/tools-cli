using DotMake.CommandLine;
using TALXIS.CLI.Data.DataModelConverter;

namespace TALXIS.CLI.Data;

[CliCommand(
    Name = "convert",
    Description = "Convert a Power Platform solution data model to various formats such as DBML, SQL, EDMX or Ribbon"
)]
public class DataModelConvertCliCommand
{
    private const string ExportsFolderName = "exports";

    [CliOption(
        Name = "--input",
        Description = "Path to the input: a solution project folder (.cdsproj/.csproj with SolutionRootPath), a declarations folder, or a .zip solution file. Defaults to the current directory.",
        Required = false
    )]
    public string? InputPath { get; set; }

    [CliOption(
        Name = "--target",
        Description = "Target format for the conversion.",
        AllowedValues = new[] { "dbml", "sql", "plainsql", "edmx", "ribbon" },
        Required = true
    )]
    public string? TargetFormat { get; set; }

    [CliOption(
        Name = "--output",
        Description = $"Directory to write the output file into. Defaults to the '{ExportsFolderName}/' folder in the current directory (auto-created and gitignored).",
        Required = false
    )]
    public string? OutputDirectory { get; set; }

    public int Run()
    {
        var inputPath = InputPath ?? Directory.GetCurrentDirectory();
        var outputDir = OutputDirectory ?? Path.Combine(Directory.GetCurrentDirectory(), ExportsFolderName);

        Directory.CreateDirectory(outputDir);
        EnsureGitIgnored(outputDir);

        var extension = TargetFormat!.ToLower() == "plainsql" ? "sql" : TargetFormat.ToLower();
        var outputFilePath = Path.Combine(outputDir, $"solution.{extension}");

        DataModelConverterService.ConvertModel(inputPath, TargetFormat!, outputFilePath);

        Console.WriteLine($"Output written to: {outputFilePath}");
        return 0;
    }

    /// <summary>
    /// Ensures the exports folder is listed in the nearest .gitignore,
    /// adding an entry if it is not already present.
    /// </summary>
    private static void EnsureGitIgnored(string exportsDirPath)
    {
        var gitIgnorePath = FindGitIgnore(exportsDirPath);
        if (gitIgnorePath == null)
            return;

        var entry = $"{ExportsFolderName}/";
        var lines = File.ReadAllLines(gitIgnorePath);
        if (lines.Any(l => l.Trim() == entry))
            return;

        File.AppendAllText(gitIgnorePath, $"{Environment.NewLine}{entry}{Environment.NewLine}");
    }

    private static string? FindGitIgnore(string startPath)
    {
        var dir = new DirectoryInfo(startPath);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, ".gitignore");
            if (File.Exists(candidate))
                return candidate;

            // Stop at the git root
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                break;

            dir = dir.Parent;
        }
        return null;
    }
}
