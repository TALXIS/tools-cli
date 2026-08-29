using System.Text.RegularExpressions;
using TALXIS.CLI.Features.Data.DataModelConverter.Model;

namespace TALXIS.CLI.Features.Data.DataModelConverter;

/// <summary>
/// Filters the attributes (columns) of a parsed data model down to those matching a set of
/// name patterns, while preserving the columns the diagram needs to stay coherent: primary keys
/// and any column that backs a relationship.
/// </summary>
public static class AttributeFilter
{
    /// <summary>
    /// Removes every column whose name does not match one of <paramref name="includePatterns"/>.
    /// Patterns are case-insensitive and support the <c>*</c> (any sequence) and <c>?</c>
    /// (single character) glob wildcards, e.g. <c>myprefix_*</c>, <c>ownerid</c>.
    /// </summary>
    public static void Apply(ParsedModel model, IReadOnlyCollection<string>? includePatterns)
    {
        ArgumentNullException.ThrowIfNull(model, nameof(model));
        ArgumentNullException.ThrowIfNull(includePatterns, nameof(includePatterns));

        var matchers = includePatterns
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(GlobToRegex)
            .ToList();

        if (matchers.Count == 0) return;

        var relationshipColumns = new HashSet<TableRow>();
        foreach (var relationship in model.relationships)
        {
            if (relationship.LeftSideRow != null) relationshipColumns.Add(relationship.LeftSideRow);
            if (relationship.RighSideRow != null) relationshipColumns.Add(relationship.RighSideRow);
        }

        foreach (var table in model.tables)
        {
            table.Rows = table.Rows
                .Where(row =>
                    row.RowType == RowType.Primarykey
                    || relationshipColumns.Contains(row)
                    || matchers.Any(rx => rx.IsMatch(row.Name)))
                .ToList();
        }
    }

    public static IReadOnlyList<string> ParsePatterns(string? commaSeparated)
    {
        if (string.IsNullOrWhiteSpace(commaSeparated)) return Array.Empty<string>();

        return commaSeparated
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static Regex GlobToRegex(string pattern)
    {
        var escaped = Regex.Escape(pattern.Trim())
            .Replace("\\*", ".*")
            .Replace("\\?", ".");

        return new Regex($"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
