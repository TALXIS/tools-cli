namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Resolves entity/attribute logical names to their Dataverse MetadataId GUIDs.
/// </summary>
public interface IMetadataIdResolver
{
    /// <summary>Resolves an entity logical name to its MetadataId.</summary>
    Task<Guid> ResolveEntityIdAsync(string? profileName, string entityLogicalName, CancellationToken ct);

    /// <summary>Resolves an attribute logical name to its MetadataId.</summary>
    Task<Guid> ResolveAttributeIdAsync(string? profileName, string entityLogicalName, string attributeLogicalName, CancellationToken ct);
}
