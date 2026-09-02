using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Features.Data.DataModelConverter.Model;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Data.DataModelConverter.AppScope;

/// <summary>
/// Narrows each table to the columns its own artefacts mention.
///
/// A reference belongs to the table whose artefact made it. Resolving one by name alone
/// keeps a column on every table that declares that name, which is a fair approximation for
/// an author's column — one table usually declares it — and useless for a platform column,
/// whose name is identical on every table in the org. One view showing createdon kept it on
/// nineteen tables in a real app.
/// </summary>
public static class AttributeReferenceFilter
{
    private static readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(AttributeReferenceFilter));

    /// <summary>Identifier-shaped tokens; column logical names are always of this shape.</summary>
    private static readonly Regex TokenPattern = new(@"[A-Za-z_][A-Za-z0-9_]{2,}", RegexOptions.Compiled);

    /// <summary>
    /// Folders whose contents reference columns. Entity declarations are deliberately not
    /// among them: an entity declares its own columns, so scanning one would report every
    /// column as referenced by itself.
    /// </summary>
    private static readonly string[] ReferencingFolders =
        ["FormXml", "SavedQueries", "Workflows", "Visualizations", "AppModuleSiteMaps", "AppModules"];

    private static readonly string[] CodeExtensions = [".cs", ".ts", ".js"];

    /// <summary>Business process flow bookkeeping. No metadata flag separates these from an
    /// author's columns, so they are named.</summary>
    private static readonly string[] ProcessFlowColumns = ["processid", "stageid", "traversedpath"];

    private const string BaseCurrencySuffix = "_base";

    public static void Apply(List<Table> tables, List<Relationship> relationships, ResolvedAppScope scope)
    {
        var references = CollectReferences(scope.SearchRoots);

        // Computed before anything is dropped. The translators read
        // Relationship.LeftSideRow/RighSideRow without a null check, so removing a row an
        // edge points at turns a narrower diagram into a crash on the sql and edmx targets.
        var loadBearing = new HashSet<TableRow>();
        foreach (var relationship in relationships)
        {
            if (relationship.LeftSideRow != null) loadBearing.Add(relationship.LeftSideRow);
            if (relationship.RighSideRow != null) loadBearing.Add(relationship.RighSideRow);
        }

        var withoutArtefacts = new List<string>();

        foreach (var table in tables.Where(t => t.Type == TableType.InSolution))
        {
            if (!references.HasOwn(table.LogicalName))
            {
                withoutArtefacts.Add(table.LogicalName);
            }

            foreach (var row in table.Rows.ToList())
            {
                // A primary key and a state model describe the table whatever refers to
                // them, and an edge's own column cannot go without crashing a translator.
                if (row.RowType is RowType.Primarykey or RowType.State or RowType.Status) continue;
                if (loadBearing.Contains(row)) continue;

                var reason = ReasonToDrop(table, row, references, scope.AuthorPrefixes);
                if (reason == null) continue;

                table.Rows.Remove(row);
                scope.DroppedColumns.Add(new DroppedColumn(table.LogicalName, row.Name, reason.Value));
            }
        }

        _logger.LogInformation(
            "Narrowed {Tables} table(s) to the columns their own forms, views, workflows, sitemaps and .cs/.ts sources refer to; "
            + "dropped {Dropped}. A dropped column is one no reference was found for, which is not the same as one that is unused.",
            tables.Count(t => t.Type == TableType.InSolution), scope.DroppedColumns.Count);

        if (withoutArtefacts.Count > 0)
        {
            // Nothing referenced these tables' columns because nothing could: they have no
            // forms or views of their own, so only their keys and relationships survive.
            _logger.LogWarning(
                "{Count} table(s) have no forms, views or charts of their own, so only their keys and relationships remain: {Tables}.",
                withoutArtefacts.Count, string.Join(", ", withoutArtefacts.OrderBy(x => x, StringComparer.Ordinal)));
        }
    }

    private static DropReason? ReasonToDrop(Table table, TableRow row, ReferenceIndex references, HashSet<string> authorPrefixes)
    {
        // Checked first, so a column dropped as plumbing is not reported as unreferenced.
        if (IsPlatformPlumbing(table, row)) return DropReason.PlatformPlumbing;

        if (references.OwnedBy(table.LogicalName, row.Name)) return null;

        // An artefact belonging to no single table — an app module, a sitemap, a plug-in —
        // can only be matched by name, and a platform column's name is the same on every
        // table in the org. Letting one rescue createdon puts it back on all nineteen
        // tables, which is the defect this filter exists to remove. An author's column is
        // named once, so the same evidence is worth trusting there: measured at 30 columns
        // across two real apps.
        if (!IsPlatformColumn(row, authorPrefixes) && references.Unattributed(row.Name)) return null;

        return DropReason.NoReferenceFound;
    }

    /// <summary>
    /// Platform plumbing a reader of the model never needs, even where something refers to it.
    /// </summary>
    private static bool IsPlatformPlumbing(Table table, TableRow row)
        => row.IsLogical == true
           || ProcessFlowColumns.Contains(row.Name, StringComparer.OrdinalIgnoreCase)
           || IsBaseCurrencyTwin(table, row);

    /// <summary>
    /// The shadow the platform maintains in the base currency beside an author's money
    /// column. Nothing in the metadata separates the two, so this is a name pairing.
    /// </summary>
    private static bool IsBaseCurrencyTwin(Table table, TableRow row)
        => row.RowType == RowType.Money
           && row.Name.EndsWith(BaseCurrencySuffix, StringComparison.OrdinalIgnoreCase)
           && table.Rows.Any(other => other.RowType == RowType.Money
               && string.Equals(other.Name, row.Name[..^BaseCurrencySuffix.Length], StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether the platform created a column rather than an author. Dataverse gives an
    /// author's column the publisher's prefix and its own columns none, which is the whole
    /// discriminator. With no prefixes to check against, nothing is called the platform's
    /// rather than narrowing the output on no evidence.
    /// </summary>
    private static bool IsPlatformColumn(TableRow row, HashSet<string> authorPrefixes)
        => authorPrefixes.Count > 0
           && !authorPrefixes.Any(prefix => row.Name.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Every token in every referencing artefact, credited to the table whose folder it sits
    /// under. An artefact outside one — an app module, a sitemap, a plug-in source — is
    /// credited to no table and matched by name alone.
    /// </summary>
    private static ReferenceIndex CollectReferences(IEnumerable<string> searchRoots)
    {
        var index = new ReferenceIndex();

        foreach (var root in searchRoots.Where(Directory.Exists).Select(Path.GetFullPath).Distinct())
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (!ShouldScan(file)) continue;

                var owner = OwnerFromPath(file);

                try
                {
                    foreach (Match match in TokenPattern.Matches(File.ReadAllText(file)))
                    {
                        index.Add(owner, match.Value);
                    }
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "Could not read {File} while looking for column references.", file);
                }
            }
        }

        return index;
    }

    /// <summary>The segment naming the entity whose folder this artefact lives under, or null
    /// for one that sits outside any and therefore speaks for many tables.</summary>
    private static string? OwnerFromPath(string file)
    {
        var segments = (Path.GetDirectoryName(file) ?? string.Empty)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (string.Equals(segments[i], "Entities", StringComparison.OrdinalIgnoreCase))
            {
                return segments[i + 1];
            }
        }

        return null;
    }

    private static bool ShouldScan(string file)
    {
        // Plug-in and script sources sit outside the declarations, so a reference from one
        // is invisible without reading them: measured at 14 columns across two real apps.
        if (CodeExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        // Entity.xml declares columns rather than referencing them.
        if (string.Equals(Path.GetFileName(file), "Entity.xml", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(file) ?? string.Empty;

        return ReferencingFolders.Any(folder =>
            directory.Contains(Path.DirectorySeparatorChar + folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || directory.EndsWith(Path.DirectorySeparatorChar + folder, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Which columns each table's own artefacts refer to, plus the references that
    /// belong to no single table and therefore count for all of them.</summary>
    private sealed class ReferenceIndex
    {
        private readonly Dictionary<string, HashSet<string>> _byTable = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _unattributed = new(StringComparer.OrdinalIgnoreCase);

        public void Add(string? table, string token)
        {
            if (table == null)
            {
                _unattributed.Add(token);
                return;
            }

            if (!_byTable.TryGetValue(table, out var tokens))
            {
                _byTable[table] = tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            tokens.Add(token);
        }

        public bool HasOwn(string table) => _byTable.ContainsKey(table);

        /// <summary>An artefact of this table names this column.</summary>
        public bool OwnedBy(string table, string column)
            => _byTable.TryGetValue(table, out var tokens) && tokens.Contains(column);

        /// <summary>Something names this column, but nothing says which table it meant.</summary>
        public bool Unattributed(string column) => _unattributed.Contains(column);
    }
}
