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
    /// dropped table cannot come back as a synthesised stub for a relationship that
    /// pointed at it.
    /// </summary>
    public static void ApplyTableScope(List<Table> tables, ResolvedAppScope scope)
    {
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
