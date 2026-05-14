using System.ComponentModel;
using System.Diagnostics;
using System.Xml.Linq;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Solution;

[CliIdempotent]
[CliLongRunning]
[CliCommand(
    Name = "import",
    Description = "Import a Dataverse solution .zip into the LIVE target environment. Requires an active profile. Accepts a .zip file, an unpacked solution folder, or a project directory (.cdsproj/.csproj). For Package Deployer packages, use 'environment package import' instead."
)]
public class SolutionImportCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(SolutionImportCliCommand));

    [CliArgument(Name = "solution-path", Description = "Path to a solution .zip file, an unpacked solution folder, or a project directory (.cdsproj/.csproj). Defaults to current directory.")]
    [DefaultValue(".")]
    public string SolutionZip { get; set; } = ".";

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
        string solutionPath = Path.GetFullPath(SolutionZip);
        string? tempZipPath = null;

        // Auto-detect input format:
        // 1. ZIP file → use directly
        // 2. Directory with Build SDK .csproj → dotnet build, use output ZIP
        // 3. Directory with *.cdsproj → unpacked solution root is <dir>/src
        // 4. Directory with Other/Solution.xml → unpacked solution folder
        // 5. Directory → treated as unpacked solution folder
        if (Directory.Exists(solutionPath))
        {
            // Check for Dataverse project convention: <project-dir>/src is the solution root.
            // *.cdsproj is the Dataverse convention (always use src/).
            // *.csproj is checked as a fallback — Build SDK projects are built directly,
            // others only used if src/ exists (avoids false positives with non-Dataverse C# projects).
            var srcFolder = Path.Combine(solutionPath, "src");
            var hasCdsProj = Directory.GetFiles(solutionPath, "*.cdsproj").Length > 0;
            var csProjFiles = Directory.GetFiles(solutionPath, "*.csproj");
            var hasCsProj = csProjFiles.Length > 0;

            // Build SDK projects: run dotnet build and use the output ZIP directly
            if (hasCsProj && !hasCdsProj)
            {
                var buildSdkProj = FindBuildSdkProject(csProjFiles);
                if (buildSdkProj is not null)
                {
                    var zipPath = await BuildAndLocateZipAsync(buildSdkProj);
                    if (zipPath is null)
                        return ExitError;
                    solutionPath = zipPath;
                }
                else if (Directory.Exists(srcFolder))
                {
                    // .csproj with src/ subfolder — likely a Dataverse project (non-Build SDK)
                    Logger.LogInformation("Found .csproj with src/ folder — using '{SrcFolder}' as solution root.", srcFolder);
                    solutionPath = srcFolder;
                }
            }

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

            // If we didn't resolve to a ZIP via Build SDK, pack the folder
            if (Directory.Exists(solutionPath))
            {
                Logger.LogInformation("Input is a folder — packing to ZIP before import...");
                tempZipPath = Path.Combine(Path.GetTempPath(), $"txc_import_{Guid.NewGuid():N}.zip");
            }
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

    /// <summary>
    /// Returns the first .csproj that uses TALXIS.DevKit.Build.Sdk, or null if none match.
    /// </summary>
    private static string? FindBuildSdkProject(string[] csProjFiles)
    {
        foreach (var proj in csProjFiles)
        {
            var content = File.ReadAllText(proj);
            if (content.Contains("TALXIS.DevKit.Build.Sdk", StringComparison.OrdinalIgnoreCase))
                return proj;
        }
        return null;
    }

    /// <summary>
    /// Runs <c>dotnet build</c> on a Build SDK project and locates the output ZIP.
    /// Returns the ZIP path on success, or null on failure.
    /// </summary>
    private async Task<string?> BuildAndLocateZipAsync(string csProjPath)
    {
        var config = Managed ? "Release" : "Debug";
        Logger.LogInformation("Building '{Project}' with configuration '{Config}'...", Path.GetFileName(csProjPath), config);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{csProjPath}\" -c {config}",
            WorkingDirectory = Path.GetDirectoryName(csProjPath),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) Logger.LogInformation("{Line}", e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Logger.LogWarning("{Line}", e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync().ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            Logger.LogError("dotnet build failed with exit code {ExitCode}.", process.ExitCode);
            return null;
        }

        // Build SDK convention: output ZIP is in bin/{config}/net462/*.zip
        var outputDir = Path.Combine(Path.GetDirectoryName(csProjPath)!, "bin", config, "net462");
        if (!Directory.Exists(outputDir))
        {
            Logger.LogError("Build output directory not found: {OutputDir}.", outputDir);
            return null;
        }

        var zipFiles = Directory.GetFiles(outputDir, "*.zip");
        if (zipFiles.Length == 0)
        {
            Logger.LogError("No .zip file found in build output directory: {OutputDir}.", outputDir);
            return null;
        }

        var zipPath = zipFiles[0];
        Logger.LogInformation("Using build output: {ZipPath}", zipPath);
        return zipPath;
    }
}
