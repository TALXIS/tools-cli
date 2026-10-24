using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Data.DataModelConverter.AppScope;

/// <summary>
/// The identifiers mentioned by the forms, views, workflows, charts, sitemaps and code beneath a
/// set of roots, credited to the table whose Entities folder a file sits under. A file outside
/// any table's folder speaks for none, so it is recorded as unattributed. Every app and every
/// source file beneath the roots counts, not only the app being converted.
/// </summary>
public sealed class ColumnReferences
{
    private static readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(ColumnReferences));

    private static readonly Regex TokenPattern = new(@"[A-Za-z_][A-Za-z0-9_]{2,}", RegexOptions.Compiled);

    private static readonly string[] ReferencingFolders =
        ["FormXml", "SavedQueries", "Workflows", "Visualizations", "AppModuleSiteMaps", "AppModules"];

    private static readonly string[] CodeExtensions = [".cs", ".ts", ".js"];

    private readonly Dictionary<string, HashSet<string>> _byTable = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unattributed = new(StringComparer.OrdinalIgnoreCase);

    public bool HasOwn(string table) => _byTable.ContainsKey(table);

    public bool OwnedBy(string table, string column)
        => _byTable.TryGetValue(table, out var tokens) && tokens.Contains(column);

    public bool Unattributed(string column) => _unattributed.Contains(column);

    public static ColumnReferences Collect(IEnumerable<string> roots)
    {
        var references = new ColumnReferences();
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        foreach (var root in roots.Where(Directory.Exists).Select(Path.GetFullPath).Distinct())
        {
            foreach (var file in SourceTree.EnumerateFiles(root, "*"))
            {
                var folders = Path.GetRelativePath(root, Path.GetDirectoryName(file)!)
                    .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (!ShouldScan(file, folders) || !seen.Add(file)) continue;

                string text;
                try
                {
                    text = File.ReadAllText(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Could not read {File} while looking for column references.", file);
                    continue;
                }

                var tokens = references.TokensOf(OwnerFrom(folders));
                foreach (Match match in TokenPattern.Matches(text))
                {
                    tokens.Add(match.Value);
                }
            }
        }

        return references;
    }

    private static bool ShouldScan(string file, string[] folders)
    {
        if (CodeExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(Path.GetFileName(file), "Entity.xml", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return folders.Any(folder => ReferencingFolders.Contains(folder, StringComparer.OrdinalIgnoreCase));
    }

    private static string? OwnerFrom(string[] folders)
    {
        for (var i = 0; i < folders.Length - 1; i++)
        {
            if (string.Equals(folders[i], "Entities", StringComparison.OrdinalIgnoreCase))
            {
                return folders[i + 1];
            }
        }

        return null;
    }

    private HashSet<string> TokensOf(string? table)
    {
        if (table == null)
        {
            return _unattributed;
        }

        if (!_byTable.TryGetValue(table, out var tokens))
        {
            _byTable[table] = tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return tokens;
    }
}
