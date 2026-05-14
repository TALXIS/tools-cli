using System.Xml.Linq;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Solution;

[CliReadOnly]
[CliCommand(
    Name = "export",
    Description = "Export a solution from the LIVE environment as a ZIP or unpacked folder. Requires an active profile."
)]
public class SolutionExportCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(SolutionExportCliCommand));

    [CliArgument(Name = "name", Description = "Solution unique name, or a project directory path (.cdsproj/.csproj) to auto-detect the solution name.")]
    public string Name { get; set; } = null!;

    [CliOption(Name = "--output", Alias = "-o", Description = "Output path (directory for unpacked, file path for ZIP). Default: current directory.", Required = false)]
    public string? Output { get; set; }

    [CliOption(Name = "--managed", Description = "Export as managed solution (default: unmanaged).", Required = false)]
    public bool Managed { get; set; }

    [CliOption(Name = "--zip", Description = "Save as raw ZIP file instead of unpacking with SolutionPackager.", Required = false)]
    public bool Zip { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var solutionName = Name;
        var outputPath = Output;

        // Detect project-dir mode: if the argument looks like a directory path, try to
        // resolve the solution unique name from the project's Solution.xml.
        if (IsDirectoryPath(solutionName))
        {
            var resolved = ResolveFromProjectDirectory(Path.GetFullPath(solutionName));
            if (resolved is null)
                return ExitValidationError;
            solutionName = resolved.Value.UniqueName;
            // Auto-set output to the solution root path so files unpack to the right place
            outputPath ??= resolved.Value.SolutionRootPath;
        }

        outputPath ??= Directory.GetCurrentDirectory();
        var unpack = !Zip;

        var options = new SolutionExportOptions(solutionName, Managed, outputPath, unpack);
        var service = TxcServices.Get<ISolutionExportService>();
        var resultPath = await service.ExportAsync(Profile, options, CancellationToken.None).ConfigureAwait(false);

        var mode = unpack ? "unpacked" : "ZIP";
        OutputFormatter.WriteData(
            new { status = "exported", solution = solutionName, managed = Managed, format = mode, path = resultPath },
            _ =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Exported solution '{solutionName}' ({(Managed ? "managed" : "unmanaged")}) → {resultPath} ({mode})");
#pragma warning restore TXC003
            });

        return ExitSuccess;
    }

    /// <summary>
    /// Returns true if the argument looks like a directory path rather than a plain solution name.
    /// </summary>
    private static bool IsDirectoryPath(string value)
    {
        if (value == ".")
            return true;
        if (value.Contains('/') || value.Contains('\\'))
            return true;
        if (Directory.Exists(value))
            return true;
        return false;
    }

    /// <summary>
    /// Resolves solution unique name and solution root path from a project directory
    /// containing a .cdsproj or .csproj with a SolutionRootPath property.
    /// </summary>
    private (string UniqueName, string SolutionRootPath)? ResolveFromProjectDirectory(string dirPath)
    {
        if (!Directory.Exists(dirPath))
        {
            Logger.LogError("Directory not found: {Path}.", dirPath);
            return null;
        }

        var projectFile = Directory.EnumerateFiles(dirPath, "*.cdsproj").FirstOrDefault()
            ?? Directory.EnumerateFiles(dirPath, "*.csproj").FirstOrDefault();

        if (projectFile is null)
        {
            Logger.LogError("No .cdsproj or .csproj found in '{Dir}'.", dirPath);
            return null;
        }

        var doc = XDocument.Load(projectFile);
        XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        var solutionRootPath = doc.Descendants(ns + "SolutionRootPath").FirstOrDefault()?.Value;

        if (string.IsNullOrWhiteSpace(solutionRootPath))
        {
            // Fallback: use src/ as the default convention
            solutionRootPath = "src";
        }

        var resolvedRoot = Path.GetFullPath(Path.Combine(dirPath, solutionRootPath));
        if (!Directory.Exists(resolvedRoot))
        {
            Logger.LogError("Solution root path '{SolutionRootPath}' does not exist at '{Resolved}'.", solutionRootPath, resolvedRoot);
            return null;
        }

        // Read the unique name from Other/Solution.xml
        var solutionXmlPath = Path.Combine(resolvedRoot, "Other", "Solution.xml");
        if (!File.Exists(solutionXmlPath))
        {
            Logger.LogError("Solution.xml not found at '{Path}'.", solutionXmlPath);
            return null;
        }

        var solutionDoc = XDocument.Load(solutionXmlPath);
        var uniqueName = solutionDoc.Descendants("UniqueName").FirstOrDefault()?.Value;
        if (string.IsNullOrWhiteSpace(uniqueName))
        {
            Logger.LogError("Could not read <UniqueName> from '{Path}'.", solutionXmlPath);
            return null;
        }

        Logger.LogInformation("Resolved solution '{UniqueName}' from project directory.", uniqueName);
        return (uniqueName, resolvedRoot);
    }
}
