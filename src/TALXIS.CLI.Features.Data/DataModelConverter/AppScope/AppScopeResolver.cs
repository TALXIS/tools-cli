using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Data.DataModelConverter.AppScope;

/// <summary>The tables a model-driven app is built on, resolved from source.</summary>
public class ResolvedAppScope
{
    public string UniqueName { get; init; } = string.Empty;

    /// <summary>Compared case-insensitively: an app component's schemaName casing is not
    /// guaranteed to match the casing of the entity's own declaration.</summary>
    public HashSet<string> TableLogicalNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every file that contributed, for reporting which sources were read.</summary>
    public List<string> SourceFiles { get; } = [];
}

/// <summary>
/// Resolves which tables an app declares, from the app module files on disk. No Dataverse
/// connection: the app's component list is in source, and so is everything it names.
/// </summary>
public static class AppScopeResolver
{
    private static readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(AppScopeResolver));

    private const string AppModulesFolder = "AppModules";
    private const string SiteMapsFolder = "AppModuleSiteMaps";

    /// <summary>The component type that carries a table's name. Views, forms, charts and
    /// workflows reference their owning table only by id, so they cannot contribute one.</summary>
    private const string EntityComponentType = "1";

    public static ResolvedAppScope Resolve(IEnumerable<string> searchRoots, string appUniqueName)
    {
        var byName = DiscoverAppModules(searchRoots);

        if (!byName.TryGetValue(appUniqueName, out var files) || files.Count == 0)
        {
            var known = byName.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            throw new InvalidOperationException(
                $"No app module named '{appUniqueName}' was found under the given inputs. "
                + (known.Count == 0
                    ? "No app modules were found at all — check that a repository root containing them was passed."
                    : $"Apps found: {string.Join(", ", known)}."));
        }

        var scope = new ResolvedAppScope { UniqueName = appUniqueName };

        // One logical app can be declared across several files: a base declaration plus
        // fragments contributed by other areas, whose components carry solutionaction="Added".
        // The app's real component set is the union of all of them.
        foreach (var file in files)
        {
            scope.SourceFiles.Add(file);
            var doc = Load(file);
            if (doc?.Root == null) continue;

            foreach (var component in doc.Root.Descendants("AppModuleComponent"))
            {
                if (component.Attribute("type")?.Value != EntityComponentType) continue;
                var schemaName = component.Attribute("schemaName")?.Value;
                if (!string.IsNullOrWhiteSpace(schemaName)) scope.TableLogicalNames.Add(schemaName);
            }
        }

        foreach (var table in ResolveSiteMapTables(searchRoots, appUniqueName))
        {
            scope.TableLogicalNames.Add(table);
        }

        _logger.LogInformation(
            "App {App} resolves to {Count} tables, from {Files} declaration file(s).",
            appUniqueName, scope.TableLogicalNames.Count, scope.SourceFiles.Count);

        return scope;
    }

    /// <summary>
    /// Every app module found, keyed by the UniqueName inside the file. The folder name is
    /// only a locator — its casing can differ from the declared name, which matters on a
    /// case-sensitive filesystem.
    /// </summary>
    public static Dictionary<string, List<string>> DiscoverAppModules(IEnumerable<string> searchRoots)
    {
        var byName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in FilesUnder(searchRoots, AppModulesFolder, "AppModule*.xml"))
        {
            var uniqueName = Load(file)?.Root?.Element("UniqueName")?.Value;
            if (string.IsNullOrWhiteSpace(uniqueName)) continue;

            if (!byName.TryGetValue(uniqueName, out var list))
            {
                byName[uniqueName] = list = [];
            }
            list.Add(file);
        }

        return byName;
    }

    /// <summary>
    /// Tables a sitemap surfaces. They appear either as an Entity attribute or as an etn
    /// parameter inside a Url, and both forms occur in the same file.
    /// </summary>
    private static IEnumerable<string> ResolveSiteMapTables(IEnumerable<string> searchRoots, string appUniqueName)
    {
        foreach (var file in FilesUnder(searchRoots, SiteMapsFolder, "AppModuleSiteMap*.xml"))
        {
            var doc = Load(file);
            if (doc?.Root == null) continue;

            var owner = doc.Root.Element("SiteMapUniqueName")?.Value;
            if (!string.Equals(owner, appUniqueName, StringComparison.OrdinalIgnoreCase)) continue;

            foreach (var element in doc.Root.Descendants())
            {
                var entity = element.Attribute("Entity")?.Value;
                if (!string.IsNullOrWhiteSpace(entity)) yield return entity;

                var url = element.Attribute("Url")?.Value;
                if (string.IsNullOrWhiteSpace(url)) continue;

                foreach (var part in url.Split('&', '?'))
                {
                    if (part.StartsWith("etn=", StringComparison.OrdinalIgnoreCase) && part.Length > 4)
                    {
                        yield return part[4..];
                    }
                }
            }
        }
    }

    /// <summary>
    /// Anchors on the component folder rather than on "Declarations": some modules keep
    /// their declarations under "CDS" instead, and a glob anchored on either name misses
    /// the other.
    /// </summary>
    private static IEnumerable<string> FilesUnder(IEnumerable<string> searchRoots, string folderName, string pattern)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in searchRoots.Where(Directory.Exists))
        {
            foreach (var folder in Directory.EnumerateDirectories(root, folderName, SearchOption.AllDirectories))
            {
                foreach (var file in Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories))
                {
                    // Managed and unmanaged copies sit side by side with identical content.
                    if (seen.Add(Path.GetFullPath(file))) yield return file;
                }
            }
        }
    }

    private static XDocument? Load(string file)
    {
        try
        {
            return XDocument.Load(file);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read app module {File}; skipping it.", file);
            return null;
        }
    }
}
