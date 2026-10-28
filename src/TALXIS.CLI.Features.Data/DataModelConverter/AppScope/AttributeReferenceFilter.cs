using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Features.Data.DataModelConverter.Model;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Data.DataModelConverter.AppScope;

/// <summary>
/// Narrows each table of an app to the columns that the files under the search roots mention. A
/// file credited to no table can only keep a column that carries a publisher prefix, since a name
/// the platform gives every table, createdon for one, would otherwise be kept everywhere by a
/// single mention.
/// </summary>
public static class AttributeReferenceFilter
{
    private static readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(AttributeReferenceFilter));

    private static readonly string[] ProcessFlowColumns = ["processid", "stageid", "traversedpath"];

    private const string BaseCurrencySuffix = "_base";

    public static void Apply(
        List<Table> tables,
        List<Relationship> relationships,
        ResolvedAppScope scope,
        IReadOnlyCollection<string> authorPrefixes,
        ILogger? logger = null)
    {
        logger ??= _logger;

        if (authorPrefixes.Count == 0)
        {
            logger.LogWarning(
                "No publisher prefix could be read from any input (Other/Solution.xml in a folder, solution.xml in a zip), "
                + "so --detail minimal cannot tell a platform column from an author's column and keeps every column a file names.");
        }

        var references = ColumnReferences.Collect(scope.SearchRoots);
        var isIncluded = AttributeFilter.Matcher(scope.IncludeAttributes);

        var edgeColumns = new HashSet<TableRow>();
        foreach (var relationship in relationships)
        {
            if (relationship.LeftSideRow != null) edgeColumns.Add(relationship.LeftSideRow);
            if (relationship.RighSideRow != null) edgeColumns.Add(relationship.RighSideRow);
        }

        var withoutFiles = new List<string>();

        foreach (var table in tables.Where(table => table.Type == TableType.InSolution))
        {
            if (!references.HasOwn(table.LogicalName))
            {
                withoutFiles.Add(table.LogicalName);
            }

            var declared = table.Rows.ToList();

            foreach (var row in declared)
            {
                if (row.RowType is RowType.Primarykey or RowType.State or RowType.Status || edgeColumns.Contains(row) || isIncluded(row.Name)) continue;

                var reason = ReasonToDrop(declared, row, table.LogicalName, references, authorPrefixes);
                if (reason == null) continue;

                table.Rows.Remove(row);
                scope.DroppedColumns.Add(new DroppedColumn(table.LogicalName, row.Name, reason));
            }
        }

        logger.LogInformation(
            "Narrowed {Tables} table(s) to the columns that files under the search roots refer to; dropped {Dropped}.",
            tables.Count(table => table.Type == TableType.InSolution), scope.DroppedColumns.Count);

        if (withoutFiles.Count > 0)
        {
            logger.LogWarning(
                "{Count} table(s) have no files of their own that refer to columns, so only the columns named elsewhere, their keys and their relationships remain: {Tables}.",
                withoutFiles.Count, string.Join(", ", withoutFiles.Order(StringComparer.Ordinal)));
        }
    }

    private static string? ReasonToDrop(
        List<TableRow> declared,
        TableRow row,
        string table,
        ColumnReferences references,
        IReadOnlyCollection<string> authorPrefixes)
    {
        if (IsPlatformPlumbing(declared, row)) return DropReason.PlatformPlumbing;

        if (references.OwnedBy(table, row.Name)) return null;

        if (!IsPlatformColumn(row, authorPrefixes) && references.Unattributed(row.Name)) return null;

        return DropReason.NoReferenceFound;
    }

    private static bool IsPlatformPlumbing(List<TableRow> declared, TableRow row)
        => row.IsLogical == true
           || ProcessFlowColumns.Contains(row.Name, StringComparer.OrdinalIgnoreCase)
           || IsBaseCurrencyTwin(declared, row);

    private static bool IsBaseCurrencyTwin(List<TableRow> declared, TableRow row)
        => row.RowType == RowType.Money
           && row.Name.EndsWith(BaseCurrencySuffix, StringComparison.OrdinalIgnoreCase)
           && declared.Any(other => other.RowType == RowType.Money
               && string.Equals(other.Name, row.Name[..^BaseCurrencySuffix.Length], StringComparison.OrdinalIgnoreCase));

    private static bool IsPlatformColumn(TableRow row, IReadOnlyCollection<string> authorPrefixes)
        => authorPrefixes.Count > 0
           && !authorPrefixes.Any(prefix => row.Name.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase));
}
