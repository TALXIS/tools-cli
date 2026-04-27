using System.ComponentModel;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Solution;

[CliIdempotent]
[CliCommand(
    Name = "import",
    Description = "Import a Dataverse solution .zip into the LIVE target environment. Requires an active profile. Accepts a .zip file, an unpacked solution folder, or a project directory (.cdsproj/.csproj). For Package Deployer packages, use 'environment package import' instead."
)]
public class SolutionImportCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(SolutionImportCliCommand));

    [CliArgument(Name = "solution-path", Description = "Path to a solution .zip file, an unpacked solution folder, or a project directory (.cdsproj/.csproj).")]
    public string SolutionZip { get; set; } = null!;

    [CliOption(Name = "--stage-and-upgrade", Description = "Use single-step upgrade when applicable.", Required = false)]
    [DefaultValue(true)]
    public bool StageAndUpgrade { get; set; } = true;

    [CliOption(Name = "--force-overwrite", Description = "Overwrite unmanaged customizations (disables SmartDiff).", Required = false)]
    public bool ForceOverwrite { get; set; }

    [CliOption(Name = "--publish-workflows", Description = "Activate workflows after import.", Required = false)]
    public bool PublishWorkflows { get; set; }

    [CliOption(Name = "--skip-dependency-check", Description = "Skip product-update dependency checks.", Required = false)]
    public bool SkipDependencyCheck { get; set; }

    [CliOption(Name = "--skip-lower-version", Description = "Skip import when source version is not higher than target.", Required = false)]
    public bool SkipLowerVersion { get; set; }

    [CliOption(Name = "--wait", Description = "Wait for completion. By default solution imports return after queueing.", Required = false)]
    public bool Wait { get; set; }

    [CliOption(Name = "--managed", Description = "When importing from a folder, pack as managed solution.", Required = false)]
    public bool Managed { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(SolutionZip))
        {
            Logger.LogError("'solution-path' argument is required.");
            return ExitValidationError;
        }

        string solutionPath = Path.GetFullPath(SolutionZip);
        string? tempZipPath = null;

        // Auto-detect input format:
        // 1. ZIP file → use directly
        // 2. Directory with *.cdsproj → unpacked solution root is <dir>/src
        // 3. Directory with Other/Solution.xml → unpacked solution folder
        // 4. Directory → treated as unpacked solution folder
        if (Directory.Exists(solutionPath))
        {
            // Check for Dataverse project convention: <project-dir>/src is the solution root.
            // *.cdsproj is the Dataverse convention (always use src/).
            // *.csproj is checked as a fallback but only used if src/ exists (avoids false
            // positives with non-Dataverse C# projects in the same directory).
            var srcFolder = Path.Combine(solutionPath, "src");
            var hasCdsProj = Directory.GetFiles(solutionPath, "*.cdsproj").Length > 0;
            var hasCsProj = Directory.GetFiles(solutionPath, "*.csproj").Length > 0;

            if (hasCdsProj)
            {
                if (Directory.Exists(srcFolder))
                {
                    Logger.LogInformation("Found .cdsproj — using '{SrcFolder}' as solution root.", srcFolder);
                    solutionPath = srcFolder;
                }
                else
                {
                    Logger.LogError("Found .cdsproj but '{SrcFolder}' does not exist.", srcFolder);
                    return ExitValidationError;
                }
            }
            else if (hasCsProj && Directory.Exists(srcFolder))
            {
                // .csproj with src/ subfolder — likely a Dataverse project
                Logger.LogInformation("Found .csproj with src/ folder — using '{SrcFolder}' as solution root.", srcFolder);
                solutionPath = srcFolder;
            }
            // Otherwise: treat directory as-is (unpacked solution folder)

            Logger.LogInformation("Input is a folder — packing to ZIP before import...");
            tempZipPath = Path.Combine(Path.GetTempPath(), $"txc_import_{Guid.NewGuid():N}.zip");
        }
        else if (!File.Exists(solutionPath))
        {
            Logger.LogError("Solution path not found: {Path}. Provide a .zip file, an unpacked solution folder, or a project directory (.cdsproj/.csproj).", solutionPath);
            return ExitValidationError;
        }

        try
        {
        // Pack folder to temp ZIP if needed (inside try/finally for cleanup)
        if (tempZipPath is not null)
        {
            var packager = TxcServices.Get<ISolutionPackagerService>();
            packager.Pack(solutionPath, tempZipPath, Managed);
            solutionPath = tempZipPath;
        }

        var options = new SolutionImportOptions(
            StageAndUpgrade: StageAndUpgrade,
            ForceOverwrite: ForceOverwrite,
            PublishWorkflows: PublishWorkflows,
            SkipDependencyCheck: SkipDependencyCheck,
            SkipLowerVersion: SkipLowerVersion,
            Async: !Wait);

        var service = TxcServices.Get<ISolutionImportService>();
        var result = await service.ImportAsync(Profile, solutionPath, options, CancellationToken.None).ConfigureAwait(false);

        var payload = new
        {
            path = FormatPath(result.Path),
            uniqueName = result.Source.UniqueName,
            sourceVersion = result.Source.Version.ToString(),
            sourceManaged = result.Source.Managed,
            existingVersion = result.ExistingTarget?.Version.ToString(),
            existingManaged = result.ExistingTarget?.Managed,
            importJobId = result.ImportJobId,
            asyncOperationId = result.AsyncOperationId,
            startedAtUtc = result.StartedAtUtc.ToString("O"),
            completedAtUtc = result.CompletedAtUtc?.ToString("O"),
            smartDiffExpected = result.SmartDiffExpected,
            status = result.Status,
        };

        OutputFormatter.WriteData(payload, _ =>
        {
#pragma warning disable TXC003
            OutputWriter.WriteLine($"Import path: {FormatPath(result.Path)}");
            OutputWriter.WriteLine($"Status: {result.Status}");
            OutputWriter.WriteLine($"ImportJobId: {result.ImportJobId}");
            if (result.AsyncOperationId is { } asyncId)
                OutputWriter.WriteLine($"AsyncOperationId: {asyncId}");
            OutputWriter.WriteLine($"Started (UTC): {result.StartedAtUtc:O}");
            if (result.CompletedAtUtc is { } completed)
                OutputWriter.WriteLine($"Completed (UTC): {completed:O}");
#pragma warning restore TXC003
        });

        return ExitSuccess;
        }
        finally
        {
            // Clean up temporary ZIP if we packed from a folder
            if (tempZipPath is not null && File.Exists(tempZipPath))
                File.Delete(tempZipPath);
        }
    }

    private static string FormatPath(SolutionImportPath path) => path switch
    {
        SolutionImportPath.Install => "install",
        SolutionImportPath.Update => "update",
        SolutionImportPath.Upgrade => "single-step upgrade",
        _ => path.ToString()
    };
}
