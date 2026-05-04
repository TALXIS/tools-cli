using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Workspace.Localization;

[CliIdempotent]
[CliCommand(
    Name = "import",
    Description = "Apply translations from a translation JSON file (or a directory of per-file JSONs from `localization export`) back into workspace XML, adding (not replacing) language-specific entries.")]
public class LocalizationImportCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(LocalizationImportCliCommand));

    [CliOption(Name = "--file", Description = "Path to a translation JSON file or a directory containing per-file translation JSONs.")]
    public required string File { get; set; }

    [CliOption(Name = "--workspace", Description = "Workspace root (defaults to current directory).", Required = false)]
    public string? Workspace { get; set; }

    [CliOption(Name = "--keep", Description = "Keep the translation file or directory passed to --file after a successful import. Default: it is deleted once everything imports cleanly. On any parse error or broken file the path is always retained for inspection.", Required = false)]
    public bool Keep { get; set; }

    protected override Task<int> ExecuteAsync()
    {
        var root = Path.GetFullPath(Workspace ?? Directory.GetCurrentDirectory());
        if (!Directory.Exists(root))
        {
            Logger.LogError("Workspace not found: {Path}", root);
            return Task.FromResult(ExitValidationError);
        }

        var inputs = ResolveInputFiles();
        if (inputs == null) return Task.FromResult(ExitValidationError);
        if (inputs.Count == 0)
        {
            Logger.LogError("No translation JSON files found at: {Path}", File);
            return Task.FromResult(ExitValidationError);
        }

        int totalAdded = 0, totalUpdated = 0, totalSkipped = 0, totalErrors = 0;
        int filesOk = 0;
        var brokenFiles = new List<string>();

        foreach (var path in inputs)
        {
            var rel = Path.GetRelativePath(root, path).Replace('\\', '/');
            var (translation, parseError) = TryReadTranslation(path);
            if (parseError != null)
            {
                Logger.LogError("Failed to parse {Path}: {Message}", rel, parseError);
                brokenFiles.Add(rel);
                totalErrors++;
                continue;
            }

            if (string.IsNullOrEmpty(translation!.TargetLanguage))
            {
                Logger.LogWarning("Skipping {Path}: no targetLanguage.", rel);
                totalErrors++;
                continue;
            }

            var result = LocalizationWriter.Apply(root, translation);
            totalAdded += result.Added;
            totalUpdated += result.Updated;
            totalSkipped += result.Skipped;
            totalErrors += result.Errors.Count;
            filesOk++;

            foreach (var err in result.Errors)
                Logger.LogWarning("{Error}", err);
        }

        // Cleanup: when everything imported cleanly, the translation file/dir
        // has served its purpose and can be removed. On any error we keep it
        // so the user can inspect / fix / retry. `--keep` overrides cleanup.
        bool fullSuccess = brokenFiles.Count == 0 && totalErrors == 0;
        bool cleanedUp = false;
        if (fullSuccess && !Keep)
        {
            cleanedUp = TryDeletePath(File);
        }

        var data = new
        {
            filesProcessed = inputs.Count,
            filesOk,
            filesBroken = brokenFiles.Count,
            broken = brokenFiles,
            added = totalAdded,
            updated = totalUpdated,
            skipped = totalSkipped,
            errors = totalErrors,
            cleanedUp,
        };
        var inputPath = File;
        OutputFormatter.WriteData(data, d =>
        {
            OutputWriter.WriteLine($"Processed {d.filesProcessed} file(s). OK: {d.filesOk}, Broken: {d.filesBroken}, Added: {d.added}, Updated: {d.updated}, Skipped: {d.skipped}");
            if (d.cleanedUp)
                OutputWriter.WriteLine($"Removed {inputPath} (pass --keep to retain).");
            if (d.broken.Count > 0)
            {
                OutputWriter.WriteLine("Broken files (could not be parsed):");
                foreach (var b in d.broken)
                    OutputWriter.WriteLine($"  - {b}");
            }
        });

        return Task.FromResult(brokenFiles.Count == 0 ? ExitSuccess : ExitError);
    }

    private static bool TryDeletePath(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
                return true;
            }
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
                return true;
            }
        }
        catch
        {
            // Best-effort cleanup; failure is non-fatal.
        }
        return false;
    }

    private static (TranslationFile? File, string? Error) TryReadTranslation(string path)
    {
        try { return (TranslationIo.Read(path), null); }
        catch (Exception ex) { return (null, ex.Message); }
    }

    private List<string>? ResolveInputFiles()
    {
        if (Directory.Exists(File))
        {
            return Directory.EnumerateFiles(File, "*.json", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        if (System.IO.File.Exists(File))
            return new List<string> { File };

        Logger.LogError("Translation file or directory not found: {Path}", File);
        return null;
    }
}
