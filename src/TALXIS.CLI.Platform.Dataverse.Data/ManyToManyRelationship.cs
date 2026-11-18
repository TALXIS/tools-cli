using Microsoft.Xrm.Sdk;

namespace TALXIS.CLI.Platform.Dataverse.Data;

/// <summary>
/// Builds the SDK <see cref="Relationship"/> for associate and disassociate requests.
/// </summary>
public static class ManyToManyRelationship
{
    /// <summary>
    /// Creates the relationship and sets the entity role when both sides are the same table.
    /// </summary>
    public static Relationship Create(string schemaName, string entityName, string targetEntityName)
    {
        var relationship = new Relationship(schemaName);
        // Dataverse rejects a reflexive N:N without a role; Referencing puts the source record on the first side.
        if (string.Equals(entityName, targetEntityName, StringComparison.OrdinalIgnoreCase)) relationship.PrimaryEntityRole = EntityRole.Referencing;
        return relationship;
    }
}
