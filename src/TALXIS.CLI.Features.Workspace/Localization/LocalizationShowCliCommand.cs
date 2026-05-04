using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Workspace.Localization;

[CliReadOnly]
[CliCommand(
    Name = "show",
    Description = "Show translation coverage for a target language across the workspace.")]
public class LocalizationShowCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(LocalizationShowCliCommand));

    [CliOption(Name = "--language", Description = "Target language (locale like cs-CZ or LCID like 1029).")]
    public required string Language { get; set; }

    [CliOption(Name = "--source-language", Description = "Source language (default: 1033 / en-US).", Required = false)]
    public string SourceLanguage { get; set; } = "1033";

    [CliOption(Name = "--workspace", Description = "Workspace root (defaults to current directory).", Required = false)]
    public string? Workspace { get; set; }

    [CliOption(Name = "--add-system-attributes", Description = "Include Power Platform system attributes (createdon, owner, statecode, etc.) in the count. Default: excluded, matching the same default on `export`.", Required = false)]
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

        var sites = AddSystemAttributes
            ? LocalizationScanner.Scan(root, sourceLcid).ToList()
            : LocalizationScanner.Scan(root, sourceLcid)
                .Where(s => !SystemAttributesFilter.ShouldExclude(s))
                .ToList();
        int total = sites.Count;
        int translated = 0;

        foreach (var site in sites)
        {
            if (HasTranslation(root, site, targetLcid))
                translated++;
        }

        int missing = total - translated;
        double coverage = total > 0 ? (double)translated / total * 100.0 : 100.0;

        var data = new
        {
            sourceLanguage = sourceLcid,
            targetLanguage = targetLcid,
            targetLocale = LanguageCodeResolver.ToLocale(targetLcid),
            total,
            translated,
            missing,
            coveragePercent = Math.Round(coverage, 2),
        };
        OutputFormatter.WriteData(data, d =>
        {
            OutputWriter.WriteLine($"Localization status for {d.targetLocale} ({d.targetLanguage})");
            OutputWriter.WriteLine($"  Total strings : {d.total}");
            OutputWriter.WriteLine($"  Translated    : {d.translated}");
            OutputWriter.WriteLine($"  Missing       : {d.missing}");
            OutputWriter.WriteLine($"  Coverage      : {d.coveragePercent:F2}%");
        });

        return Task.FromResult(ExitSuccess);
    }

    private static bool HasTranslation(string workspaceRoot, LocalizableSite site, string targetLcid)
    {
        var path = Path.Combine(workspaceRoot, site.FileRelativePath.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            var doc = System.Xml.Linq.XDocument.Load(path, System.Xml.Linq.LoadOptions.PreserveWhitespace);
            var source = LocalizationScanner.LocateByXPath(doc, site.XPath);
            if (source?.Parent == null) return false;
            foreach (var sibling in source.Parent.Elements(source.Name))
            {
                if (ReferenceEquals(sibling, source)) continue;
                var attr = sibling.Attribute(site.LanguageAttr);
                if (attr != null && attr.Value == targetLcid)
                    return true;
            }
        }
        catch
        {
            // ignore unreadable files
        }
        return false;
    }
}
