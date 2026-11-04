namespace TALXIS.CLI.Features.Environment.Data.Query;

/// <summary>
/// Recognizes OData paths that target table metadata (EntityDefinitions) and points to the commands built for it.
/// </summary>
internal static class ODataMetadataPathHint
{
    private const string EntityDefinitions = "EntityDefinitions";

    public static bool IsEntityDefinitionsPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        var trimmed = path.Trim().TrimStart('/');
        if (!trimmed.StartsWith(EntityDefinitions, StringComparison.OrdinalIgnoreCase)) return false;

        return trimmed.Length == EntityDefinitions.Length
            || trimmed[EntityDefinitions.Length] is '(' or '/' or '?';
    }

    public static string? For(string? path) =>
        IsEntityDefinitionsPath(path)
            ? "Metadata queries (EntityDefinitions) accept only part of OData (no --top or $orderby) and fail on an unknown table name. "
                + "Use 'txc env entity describe <entity>' for a table's columns, or 'txc env entity list --search <text>' to find tables."
            : null;
}
