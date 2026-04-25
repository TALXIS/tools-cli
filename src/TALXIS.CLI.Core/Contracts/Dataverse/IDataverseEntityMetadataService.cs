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
/// Entity-level metadata detail returned by
/// <see cref="IDataverseEntityMetadataService.GetEntityDetailAsync"/>.
/// </summary>
public sealed record EntityDetailRecord(
    string LogicalName,
    string SchemaName,
    string? DisplayName,
    string? PluralDisplayName,
    string? Description,
    int? EntityTypeCode,
    string OwnershipType,
    string? PrimaryIdAttribute,
    string? PrimaryNameAttribute,
    bool IsCustomEntity,
    bool IsActivity,
    bool IsAuditEnabled,
    bool ChangeTrackingEnabled,
    string? EntitySetName,
    string? CollectionSchemaName,
    bool IsCustomizable,
    string? TableType,
    bool HasNotes,
    bool HasActivities);

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
    /// Creates an attribute (column) on the specified entity using a strongly-typed options object.
    /// </summary>
    Task CreateAttributeAsync(
        string? profileName,
        CreateAttributeOptions options,
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

    /// <summary>
    /// Creates a new entity (table) in Dataverse using the given options.
    /// </summary>
    Task CreateEntityAsync(
        string? profileName,
        CreateEntityOptions options,
        CancellationToken ct);

    /// <summary>
    /// Deletes an entity (table) from Dataverse by its logical name.
    /// </summary>
    Task DeleteEntityAsync(
        string? profileName,
        string entityLogicalName,
        CancellationToken ct);

    /// <summary>
    /// Updates an existing attribute (column) on a Dataverse entity.
    /// Only the fields that are explicitly provided (non-null) are modified.
    /// </summary>
    Task UpdateAttributeAsync(
        string? profileName,
        string entityLogicalName,
        string attributeLogicalName,
        string? displayName,
        string? description,
        string? requiredLevel,
        CancellationToken ct);

    /// <summary>
    /// Retrieves detailed metadata for a single attribute, including type-specific properties.
    /// </summary>
    Task<Dictionary<string, object?>> GetAttributeDetailAsync(
        string? profileName,
        string entityLogicalName,
        string attributeLogicalName,
        CancellationToken ct);

    /// <summary>
    /// Deletes an attribute (column) from a Dataverse entity.
    /// </summary>
    Task DeleteAttributeAsync(
        string? profileName,
        string entityLogicalName,
        string attributeLogicalName,
        CancellationToken ct);

    /// <summary>
    /// Deletes a relationship from Dataverse by its schema name.
    /// </summary>
    Task DeleteRelationshipAsync(
        string? profileName,
        string relationshipSchemaName,
        CancellationToken ct);

    /// <summary>
    /// Retrieves entity-level metadata (not columns) for a specific entity.
    /// </summary>
    Task<EntityDetailRecord> GetEntityDetailAsync(
        string? profileName,
        string entityLogicalName,
        CancellationToken ct);

    /// <summary>
    /// Updates entity-level metadata (display name, plural name, description).
    /// Only the fields that are explicitly provided (non-null) are modified.
    /// </summary>
    Task UpdateEntityAsync(
        string? profileName,
        string entityLogicalName,
        string? displayName,
        string? pluralName,
        string? description,
        CancellationToken ct);
}
