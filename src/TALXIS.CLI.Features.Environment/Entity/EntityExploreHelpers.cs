using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>
/// Pure formatting helpers for <c>entity explore</c> output.
/// </summary>
public static class EntityExploreHelpers
{
    /// <summary>
    /// Splits the compact "value:label, value:label" option string produced by
    /// entity metadata into display lines like "375970000 = Box".
    /// </summary>
    public static IReadOnlyList<string> OptionLines(string? optionValues)
    {
        if (string.IsNullOrWhiteSpace(optionValues)) return Array.Empty<string>();

        return optionValues
            .Split(", ", StringSplitOptions.RemoveEmptyEntries)
            .Select(pair =>
            {
                var parts = pair.Split(':', 2);
                return parts.Length == 2 ? $"{parts[0]} = {parts[1]}" : pair;
            })
            .ToList();
    }

    /// <summary>Maps the metadata RequiredLevel to the short display used in the columns table.</summary>
    public static string RequiredDisplay(string? requiredLevel) => requiredLevel switch
    {
        "ApplicationRequired" or "SystemRequired" => "Required",
        "Recommended" => "Recommended",
        _ => ""
    };

    /// <summary>Maps the relationship type name to the compact 1:N / N:1 / N:N form.</summary>
    public static string RelationshipTypeShort(string relationshipType) => relationshipType switch
    {
        "OneToMany" => "1:N",
        "ManyToOne" => "N:1",
        "ManyToMany" => "N:N",
        _ => relationshipType
    };

    /// <summary>Returns the entity on the other side of the relationship from <paramref name="self"/>.</summary>
    public static string RelatedEntity(EntityRelationshipRecord relationship, string self)
        => string.Equals(relationship.Entity1LogicalName, self, StringComparison.OrdinalIgnoreCase)
            ? relationship.Entity2LogicalName
            : relationship.Entity1LogicalName;

    /// <summary>Maps a systemform type code to its display name.</summary>
    public static string FormTypeName(int formType) => formType switch
    {
        2 => "Main",
        5 => "Mobile",
        6 => "Quick View",
        7 => "Quick Create",
        8 => "Dialog",
        11 => "Card",
        12 => "Main Interactive",
        _ => $"Type {formType}"
    };
}
