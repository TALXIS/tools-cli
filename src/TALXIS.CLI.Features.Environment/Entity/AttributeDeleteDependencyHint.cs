namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>
/// Recognizes a Dataverse "referenced by N other components" delete failure and points to the command that lists the blockers.
/// </summary>
internal static class AttributeDeleteDependencyHint
{
    public static bool IsBlockedByDependencies(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.Contains("referenced by", StringComparison.OrdinalIgnoreCase)
                && message.Contains("other component", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static string? For(Exception ex, string entity, string attribute) =>
        IsBlockedByDependencies(ex)
            ? $"Run 'txc env component dependency list --entity {entity} --attribute {attribute}' to see which components reference the attribute."
            : null;
}
