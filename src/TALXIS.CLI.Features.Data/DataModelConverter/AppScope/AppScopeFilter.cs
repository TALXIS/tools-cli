using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Features.Data.DataModelConverter.Model;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Data.DataModelConverter.AppScope;

/// <summary>Narrows a parsed model to the tables an app is built on.</summary>
public static class AppScopeFilter
{
    private static readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(AppScopeFilter));

    /// <summary>
    /// Drops tables the app does not declare. Runs before relationships are built, so a
    /// dropped table cannot come back as a stub for a relationship that pointed at it.
    /// </summary>
    public static void ApplyTableScope(List<Table> tables, ResolvedAppScope scope)
    {
        foreach (var table in tables.Where(table => table.Type == TableType.InSolution))
        {
            scope.AllDeclaredTableLogicalNames.Add(table.LogicalName);
        }

        var removed = tables.RemoveAll(table =>
            table.Type == TableType.InSolution && !scope.TableLogicalNames.Contains(table.LogicalName));

        var missing = scope.TableLogicalNames
            .Where(name => !tables.Any(table => string.Equals(table.LogicalName, name, StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.Ordinal)
            .ToList();

        _logger.LogInformation(
            "Scoped to app {App}: kept {Kept} table(s), dropped {Dropped} not declared by it.",
            scope.UniqueName, tables.Count, removed);

        if (missing.Count > 0)
        {
            _logger.LogWarning(
                "App {App} references {Count} table(s) that none of the given inputs declare: {Tables}.",
                scope.UniqueName, missing.Count, string.Join(", ", missing));
        }
    }

    /// <summary>
    /// Drops the option sets no remaining column refers to. A step that removes columns after
    /// this has run needs another call.
    /// </summary>
    public static void RemoveUnusedOptionSets(ParsedModel model)
    {
        var referenced = model.tables
            .SelectMany(table => table.Rows)
            .Select(row => row.OptionSetName)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        model.optionSets.RemoveAll(optionSet => !referenced.Contains(optionSet.LocalizedName));
    }
}
