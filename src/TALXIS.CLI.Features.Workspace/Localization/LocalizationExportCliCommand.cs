using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Workspace.Localization;

[CliReadOnly]
[CliCommand(
    Name = "export",
    Description = "Extract all localizable strings from the workspace into one translation JSON file per source XML file (mirrors workspace structure under <output>/).")]
public class LocalizationExportCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(LocalizationExportCliCommand));

    [CliOption(Name = "--language", Description = "Target language to translate into (locale like cs-CZ or LCID like 1029).")]
    public required string Language { get; set; }

    [CliOption(Name = "--source-language", Description = "Source language to extract from (default: en-US / 1033).", Required = false)]
    public string SourceLanguage { get; set; } = "1033";

    [CliOption(Name = "--workspace", Description = "Workspace root (defaults to current directory).", Required = false)]
    public string? Workspace { get; set; }

    [CliOption(Name = "--output", Description = "Output directory for per-file translation JSONs (default: ./translations-<language>). Mirrors workspace folder structure with .json instead of .xml.", Required = false)]
    public string? Output { get; set; }

    [CliOption(Name = "--only-missing", Description = "Include only strings that have no translation yet in the target language (default: true).", Required = false)]
    public bool OnlyMissing { get; set; } = true;

    [CliOption(Name = "--single-file", Description = "Legacy single-file mode: write all entries into the path given by --output instead of per-file JSONs.", Required = false)]
    public bool SingleFile { get; set; } = false;

    [CliOption(Name = "--add-system-attributes", Description = "Include Power Platform system attributes (createdon, owner, statecode, etc.) in Entity.xml exports. Default: excluded — these are localized by the platform itself based on the user's locale.", Required = false)]
    public bool AddSystemAttributes { get; set; }

    protected override Task<int> ExecuteAsync()
    {
        var root = Path.GetFullPath(Workspace ?? Directory.GetCurrentDirectory());
        if (!Directory.Exists(root))
        {
            Logger.LogError("Workspace not found: {Path}", root);
            return Task.FromResult(ExitValidationError);
        }

        var sourceLcid = LanguageCodeResolver.Resolve(SourceLanguage);
        var targetLcid = LanguageCodeResolver.Resolve(Language);
        var locale = LanguageCodeResolver.ToLocale(targetLcid);
        var generatedAt = DateTime.UtcNow.ToString("o");

        var sites = AddSystemAttributes
            ? LocalizationScanner.Scan(root, sourceLcid).ToList()
            : LocalizationScanner.Scan(root, sourceLcid)
                .Where(s => !SystemAttributesFilter.ShouldExclude(s))
                .ToList();

        if (SingleFile)
        {
            var singlePath = Output ?? Path.Combine(root, $"translations-{locale}.json");
            return Task.FromResult(WriteSingleFile(root, sites, sourceLcid, targetLcid, locale, generatedAt, singlePath));
        }

        var outDir = Output ?? Path.Combine(root, $"translations-{locale}");
        return Task.FromResult(WritePerFile(root, sites, sourceLcid, targetLcid, locale, generatedAt, outDir));
    }

    private int WriteSingleFile(string root, List<LocalizableSite> sites, string sourceLcid, string targetLcid, string locale, string generatedAt, string outPath)
    {
        var file = new TranslationFile
        {
            SourceLanguage = sourceLcid,
            TargetLanguage = targetLcid,
            GeneratedAt = generatedAt,
            Workspace = root,
        };

        foreach (var site in sites)
        {
            var existing = LoadExistingTranslation(root, site, targetLcid);
            if (OnlyMissing && existing != null) continue;
            file.Strings.Add(BuildUnit(site, existing));
        }

        TranslationIo.Write(outPath, file);

        var data = new
        {
            mode = "single-file",
            count = file.Strings.Count,
            targetLanguage = targetLcid,
            targetLocale = locale,
            outputPath = outPath,
        };
        OutputFormatter.WriteData(data, d =>
            OutputWriter.WriteLine($"Exported {d.count} string(s) for {d.targetLocale} ({d.targetLanguage}) to {d.outputPath}"));
        return ExitSuccess;
    }

    private int WritePerFile(string root, List<LocalizableSite> sites, string sourceLcid, string targetLcid, string locale, string generatedAt, string outDir)
    {
        Directory.CreateDirectory(outDir);

        int filesWritten = 0;
        int totalStrings = 0;

        foreach (var group in sites.GroupBy(s => s.FileRelativePath))
        {
            var bucket = new TranslationFile
            {
                SourceLanguage = sourceLcid,
                TargetLanguage = targetLcid,
                GeneratedAt = generatedAt,
                Workspace = root,
            };

            foreach (var site in group)
            {
                var existing = LoadExistingTranslation(root, site, targetLcid);
                if (OnlyMissing && existing != null) continue;
                bucket.Strings.Add(BuildUnit(site, existing));
            }

            if (bucket.Strings.Count == 0) continue;

            // Mirror workspace structure: src/.../Entity.xml -> outDir/src/.../Entity.json
            var jsonRel = Path.ChangeExtension(group.Key, ".json");
            var outFile = Path.Combine(outDir, jsonRel.Replace('/', Path.DirectorySeparatorChar));
            TranslationIo.Write(outFile, bucket);

            filesWritten++;
            totalStrings += bucket.Strings.Count;
        }

        var data = new
        {
            mode = "per-file",
            filesWritten,
            count = totalStrings,
            targetLanguage = targetLcid,
            targetLocale = locale,
            outputDir = outDir,
        };
        OutputFormatter.WriteData(data, d =>
            OutputWriter.WriteLine($"Exported {d.count} string(s) across {d.filesWritten} file(s) for {d.targetLocale} ({d.targetLanguage}) to {d.outputDir}"));
        return ExitSuccess;
    }

    private static TranslationUnit BuildUnit(LocalizableSite site, string? existing) => new()
    {
        Id = site.Id,
        File = site.FileRelativePath,
        XPath = site.XPath,
        LanguageAttr = site.LanguageAttr,
        ValueAttr = site.ValueAttr,
        Source = site.Source,
        Target = existing,
    };

    private static string? LoadExistingTranslation(string workspaceRoot, LocalizableSite site, string targetLcid)
    {
        var path = Path.Combine(workspaceRoot, site.FileRelativePath.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            var doc = System.Xml.Linq.XDocument.Load(path, System.Xml.Linq.LoadOptions.PreserveWhitespace);
            var source = LocalizationScanner.LocateByXPath(doc, site.XPath);
            if (source?.Parent == null) return null;
            foreach (var sibling in source.Parent.Elements(source.Name))
            {
                if (ReferenceEquals(sibling, source)) continue;
                var attr = sibling.Attribute(site.LanguageAttr);
                if (attr == null || attr.Value != targetLcid) continue;
                if (site.ValueAttr != null)
                    return sibling.Attribute(site.ValueAttr)?.Value;
                return sibling.Value;
            }
        }
        catch
        {
            // ignore
        }
        return null;
    }
}
