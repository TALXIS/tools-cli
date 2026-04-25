namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Summary row for an entity in the environment, returned by
/// <see cref="IDataverseEntityMetadataService.ListEntitiesAsync"/>.
/// </summary>
public sealed record EntitySummaryRecord(
    string LogicalName,
    string? SchemaName,
    string? DisplayName,
    string? EntitySetName,
    bool IsCustomEntity);

/// <summary>
/// Column/attribute metadata for a single entity, returned by
/// <see cref="IDataverseEntityMetadataService.DescribeEntityAsync"/>.
/// </summary>
public sealed record EntityAttributeRecord(
    string LogicalName,
    string? SchemaName,
    string? DisplayName,
    string AttributeTypeName,
    bool IsCustomAttribute,
    bool IsPrimaryId,
    bool IsPrimaryName,
    int? MaxLength,
    string? Description);

/// <summary>
/// Relationship summary for an entity, returned by
/// <see cref="IDataverseEntityMetadataService.ListRelationshipsAsync"/>.
/// </summary>
public sealed record EntityRelationshipRecord(
    string SchemaName,
    string RelationshipType,
    string Entity1LogicalName,
    string Entity2LogicalName,
    bool IsCustomRelationship,
    string? IntersectEntityName);

/// <summary>
/// Schema/metadata introspection for Dataverse entities. All calls go
/// through <c>ServiceClient</c> using the metadata API
/// (<c>RetrieveAllEntitiesRequest</c> / <c>RetrieveEntityRequest</c>).
/// </summary>
public interface IDataverseEntityMetadataService
{
    /// <summary>
    /// Lists all entities in the environment, optionally filtering by
    /// a search term (matched against logical name, schema name, or display name).
    /// </summary>
    Task<IReadOnlyList<EntitySummaryRecord>> ListEntitiesAsync(
        string? profileName,
        string? search,
        bool includeSystem,
        CancellationToken ct);

    /// <summary>
    /// Describes columns/attributes for a specific entity.
    /// </summary>
    Task<IReadOnlyList<EntityAttributeRecord>> DescribeEntityAsync(
        string? profileName,
        string entityLogicalName,
        bool includeSystem,
        CancellationToken ct);

    /// <summary>
    /// Creates an attribute (column) on the specified entity.
    /// </summary>
    Task CreateAttributeAsync(
        string? profileName,
        string entityLogicalName,
        string schemaName,
        string displayName,
        string type,
        bool required,
        string? targetEntity,
        string[]? options,
        CancellationToken ct);

    /// <summary>
    /// Creates a many-to-many relationship between two entities.
    /// </summary>
    Task CreateManyToManyRelationshipAsync(
        string? profileName,
        string entity1,
        string entity2,
        string schemaName,
        string? displayName,
        CancellationToken ct);

    /// <summary>
    /// Lists all relationships (1:N, N:1, N:N) for the specified entity.
    /// </summary>
    Task<IReadOnlyList<EntityRelationshipRecord>> ListRelationshipsAsync(
        string? profileName,
        string entityLogicalName,
        CancellationToken ct);
}
