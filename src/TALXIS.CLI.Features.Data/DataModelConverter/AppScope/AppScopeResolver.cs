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

    /// <summary>Compared case-insensitively: a component's schemaName casing can differ
    /// from the casing of the entity's own declaration.</summary>
    public HashSet<string> TableLogicalNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> SourceFiles { get; } = [];

    /// <summary>Every table an input declares, captured before scoping removes the ones the
    /// app does not use.</summary>
    public HashSet<string> AllDeclaredTableLogicalNames { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Resolves which tables an app declares from the app module files on disk, with no
/// Dataverse connection.
/// </summary>
public static class AppScopeResolver
{
    private static readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(AppScopeResolver));

    private const string AppModulesFolder = "AppModules";
    private const string SiteMapsFolder = "AppModuleSiteMaps";
    private const string EntityComponentType = "1";
    private const string RemovedAction = "Removed";

    public static ResolvedAppScope Resolve(IEnumerable<string> searchRoots, string appUniqueName)
    {
        var roots = searchRoots.ToList();
        var byName = DiscoverAppModules(roots);

        if (!byName.TryGetValue(appUniqueName, out var files) || files.Count == 0)
        {
            var known = byName.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            throw new ArgumentException(known.Count == 0
                ? "No app modules were found. They are read from unpacked source under --root, or under the inputs when no --root is given."
                : $"No app module named '{appUniqueName}' was found. Apps found: {string.Join(", ", known)}.");
        }

        var scope = new ResolvedAppScope { UniqueName = appUniqueName };
        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            scope.SourceFiles.Add(file);
            var doc = Load(file);
            if (doc?.Root == null) continue;

            foreach (var component in doc.Root.Descendants("AppModuleComponent"))
            {
                if (component.Attribute("type")?.Value != EntityComponentType) continue;

                var schemaName = component.Attribute("schemaName")?.Value;
                if (string.IsNullOrWhiteSpace(schemaName)) continue;

                if (string.Equals(component.Attribute("solutionaction")?.Value, RemovedAction, StringComparison.OrdinalIgnoreCase))
                {
                    removed.Add(schemaName);
                }
                else
                {
                    scope.TableLogicalNames.Add(schemaName);
                }
            }
        }

        foreach (var table in ResolveSiteMapTables(roots, appUniqueName))
        {
            scope.TableLogicalNames.Add(table);
        }

        scope.TableLogicalNames.ExceptWith(removed);

        _logger.LogInformation(
            "App {App} resolves to {Count} tables, from {Files} declaration file(s).",
            appUniqueName, scope.TableLogicalNames.Count, scope.SourceFiles.Count);

        return scope;
    }

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

    private static IEnumerable<string> FilesUnder(IEnumerable<string> searchRoots, string folderName, string pattern)
    {
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        foreach (var root in searchRoots.Where(Directory.Exists))
        {
            foreach (var file in SourceTree.EnumerateFiles(root, pattern))
            {
                var folders = Path.GetRelativePath(root, Path.GetDirectoryName(file)!)
                    .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (folders.Contains(folderName, StringComparer.OrdinalIgnoreCase) && seen.Add(Path.GetFullPath(file)))
                {
                    yield return file;
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
            _logger.LogWarning(ex, "Could not read {File}; skipping it.", file);
            return null;
        }
    }
}
