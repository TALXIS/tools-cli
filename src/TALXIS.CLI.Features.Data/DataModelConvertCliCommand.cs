using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Features.Data.DataModelConverter;
using TALXIS.CLI.Features.Data.DataModelConverter.AppScope;
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
        Name = "--detail",
        Description = "How much to emit. 'full' is everything the inputs declare. 'minimal' shows how the app was built: each table keeps only the columns its own forms, views, workflows, sitemap and .cs/.ts sources refer to, platform plumbing is dropped, and an N:N appears only when both its tables belong to the app. A dropped column is one no reference was found for, which is not the same as one that is unused: a name built at runtime cannot be found at all. 'minimal' requires --app, and is not a schema export -- use 'full' to generate SQL or EDMX for tooling.",
        AllowedValues = new[] { "full", "minimal" },
        Required = false
    )]
    public string Detail { get; set; } = "full";

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
        var detail = string.Equals(Detail, "minimal", StringComparison.OrdinalIgnoreCase)
            ? DetailLevel.Minimal
            : DetailLevel.Full;

        if (detail == DetailLevel.Minimal && string.IsNullOrWhiteSpace(AppUniqueName))
        {
            throw new ArgumentException("--detail minimal narrows an app's tables to how that app uses them, so it requires --app.");
        }

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

        var droppedColumns = DataModelConverterService.ConvertModel(
            inputPaths, TargetFormat!, outputFilePath, AppUniqueName, appSearchRoots, detail);

        var summary = new ConvertSummary(
            outputFilePath,
            Detail.ToLowerInvariant(),
            droppedColumns.Count,
            [.. droppedColumns.GroupBy(c => c.Reason)
                              .OrderBy(g => g.Key)
                              .Select(g => new DroppedByReason(g.Key.ToString(), g.Count()))],
            droppedColumns);

        // Every dropped column in one warning is unreadable once there are thousands of
        // them, so the full list goes to the data channel and text mode gets the counts.
        OutputFormatter.WriteData(summary, s =>
        {
            OutputWriter.WriteLine($"Output written to: {s.OutputFile}");
            foreach (var reason in s.DroppedByReason)
            {
                OutputWriter.WriteLine($"  dropped {reason.Count} column(s): {reason.Reason}");
            }
        });

        return Task.FromResult(ExitSuccess);
    }

    /// <param name="OutputFile">Where the converted model was written.</param>
    /// <param name="Detail">The detail level the conversion ran at.</param>
    /// <param name="ColumnsDropped">How many columns were left out in total.</param>
    /// <param name="DroppedByReason">Counts per reason, which is what a reader needs first.</param>
    /// <param name="DroppedColumns">Every dropped column, for a caller that wants to check one.</param>
    public sealed record ConvertSummary(
        string OutputFile,
        string Detail,
        int ColumnsDropped,
        IReadOnlyList<DroppedByReason> DroppedByReason,
        IReadOnlyList<DroppedColumn> DroppedColumns);

    /// <param name="Reason">Why these columns were left out.</param>
    /// <param name="Count">How many were left out for that reason.</param>
    public sealed record DroppedByReason(string Reason, int Count);

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
