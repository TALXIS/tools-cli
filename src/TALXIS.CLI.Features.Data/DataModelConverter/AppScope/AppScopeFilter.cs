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
    /// relationship out of the app cannot bring its referencing table back as a stub. A
    /// relationship <em>into</em> a dropped table still stubs the far side deliberately, so
    /// a lookup terminates somewhere visible — see <see cref="Table.StubDeclaredOutsideApp"/>
    /// for how the diagram tells that apart from a table no input declares.
    /// </summary>
    public static void ApplyTableScope(List<Table> tables, ResolvedAppScope scope)
    {
        // The one point where every input's declarations are still present.
        foreach (var table in tables.Where(t => t.Type == TableType.InSolution))
        {
            scope.AllDeclaredTableLogicalNames.Add(table.LogicalName);
        }

        var removed = tables.RemoveAll(t =>
            t.Type == TableType.InSolution && !scope.TableLogicalNames.Contains(t.LogicalName));

        var missing = scope.TableLogicalNames
            .Where(name => !tables.Any(t => string.Equals(t.LogicalName, name, System.StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => x)
            .ToList();

        _logger.LogInformation(
            "Scoped to app {App}: kept {Kept} table(s), dropped {Dropped} not declared by it.",
            scope.UniqueName, tables.Count, removed);

        if (missing.Count > 0)
        {
            // The app names them but no input declares them — usually a module that was
            // not passed in, which would otherwise show up only as an oddly small diagram.
            _logger.LogWarning(
                "App {App} references {Count} table(s) that none of the given inputs declare: {Tables}.",
                scope.UniqueName, missing.Count, string.Join(", ", missing));
        }
    }
}
