using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Features.Data.DataModelConverter;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Data;

[CliReadOnly]
[CliWorkflow("local-development")]
[CliCommand(
    Name = "convert",
    Description = "Convert a Power Platform solution data model to various formats such as DBML, SQL, EDMX or Ribbon"
)]
public class DataModelConvertCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(DataModelConvertCliCommand));
    private const string ExportsFolderName = "exports";

    [CliOption(
        Name = "--input",
        Aliases = ["-i"],
        Description = "Path to an input: a solution project folder (.cdsproj/.csproj with SolutionRootPath), a declarations folder, or a .zip solution file. Can be specified multiple times to merge several sources into one model; earlier inputs win where two declare the same attribute differently. Defaults to the current directory.",
        Required = false
    )]
    public List<string> InputPaths { get; set; } = [];

    [CliOption(
        Name = "--root",
        Description = "Path to a repository root; every declarations folder beneath it becomes an input. Can be specified multiple times. Use this rather than listing folders when a project's model spans many modules, and pass the product repository as a second root when the base model lives there.",
        Required = false
    )]
    public List<string> Roots { get; set; } = [];

    [CliOption(
        Name = "--app",
        Description = "Unique name of a model-driven app. Narrows the output to the tables that app is built on, instead of everything the inputs declare. App modules are searched for under --root, or under the inputs when no root is given.",
        Required = false
    )]
    public string? AppUniqueName { get; set; }

    [CliOption(
        Name = "--target",
        Description = "Target format for the conversion.",
        AllowedValues = new[] { "dbml", "sql", "plainsql", "edmx", "ribbon" },
        Required = true
    )]
    public string? TargetFormat { get; set; }

    [CliOption(
        Name = "--output",
        Aliases = ["-o"],
        Description = $"Directory path to write the output file into. Defaults to the '{ExportsFolderName}/' folder in the current directory (auto-created and gitignored).",
        Required = false
    )]
    public string? OutputDirectory { get; set; }

    protected override Task<int> ExecuteAsync()
    {
        var inputPaths = new List<string>(InputPaths);

        foreach (var root in Roots)
        {
            var discovered = DataModelConverterService.DiscoverDeclarationFolders(root);
            if (discovered.Count == 0)
            {
                Logger.LogWarning("No declarations were found under root {Root}.", root);
            }
            inputPaths.AddRange(discovered);
        }

        // Scoping to an app needs to reach the module that declares it, which is not the
        // module that declares the entities -- so when only an app is named, search from
        // the enclosing repository rather than the working directory alone.
        var appSearchRoots = new List<string>(Roots);
        if (!string.IsNullOrWhiteSpace(AppUniqueName) && appSearchRoots.Count == 0)
        {
            var enclosing = FindEnclosingRepositoryRoot(Directory.GetCurrentDirectory());
            appSearchRoots.Add(enclosing);
            Logger.LogInformation("Searching for app modules under {Root}.", enclosing);
        }

        if (inputPaths.Count == 0)
        {
            inputPaths.Add(Directory.GetCurrentDirectory());
        }
        var outputDir = OutputDirectory ?? Path.Combine(Directory.GetCurrentDirectory(), ExportsFolderName);

        Directory.CreateDirectory(outputDir);
        EnsureGitIgnored(outputDir);

        var extension = TargetFormat!.ToLower() == "plainsql" ? "sql" : TargetFormat.ToLower();
        var outputFilePath = Path.Combine(outputDir, $"solution.{extension}");

        DataModelConverterService.ConvertModel(inputPaths, TargetFormat!, outputFilePath, AppUniqueName, appSearchRoots);

        OutputFormatter.WriteResult("succeeded", $"Output written to: {outputFilePath}");
        return Task.FromResult(ExitSuccess);
    }

    /// <summary>
    /// Walks up for the repository that encloses a directory, so an app can be found
    /// without the caller naming a root. Falls back to the directory itself.
    /// </summary>
    private static string FindEnclosingRepositoryRoot(string startPath)
    {
        var dir = new DirectoryInfo(startPath);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || dir.GetFiles("*.sln").Length > 0)
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return startPath;
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

        File.AppendAllText(gitIgnorePath, $"{System.Environment.NewLine}{entry}{System.Environment.NewLine}");
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
