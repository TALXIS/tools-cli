using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Data;

/// <summary>
/// Dataverse implementation of <see cref="IDataverseEntityMetadataService"/>.
/// Uses the metadata API (<c>RetrieveAllEntitiesRequest</c> /
/// <c>RetrieveEntityRequest</c>) to provide entity discovery and schema
/// introspection for the connected environment.
/// </summary>
internal sealed class DataverseEntityMetadataService : IDataverseEntityMetadataService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<EntitySummaryRecord>> ListEntitiesAsync(
        string? profileName, string? search, bool includeSystem, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        var request = new RetrieveAllEntitiesRequest
        {
            EntityFilters = EntityFilters.Entity,
            RetrieveAsIfPublished = true
        };

        var response = (RetrieveAllEntitiesResponse)
            await conn.Client.ExecuteAsync(request, ct).ConfigureAwait(false);

        IEnumerable<EntityMetadata> entities = response.EntityMetadata;

        // Filter out non-customizable system entities unless explicitly requested.
        if (!includeSystem)
        {
            entities = entities.Where(e =>
                e.IsCustomEntity == true || e.IsCustomizable?.Value == true);
        }

        // Apply search filter across logical name, schema name, and display name.
        if (!string.IsNullOrWhiteSpace(search))
        {
            entities = entities.Where(e =>
                Contains(e.LogicalName, search) ||
                Contains(e.SchemaName, search) ||
                Contains(e.DisplayName?.UserLocalizedLabel?.Label, search));
        }

        return entities
            .OrderBy(e => e.LogicalName, StringComparer.OrdinalIgnoreCase)
            .Select(e => new EntitySummaryRecord(
                LogicalName: e.LogicalName,
                SchemaName: e.SchemaName,
                DisplayName: e.DisplayName?.UserLocalizedLabel?.Label,
                EntitySetName: e.EntitySetName,
                IsCustomEntity: e.IsCustomEntity == true))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EntityAttributeRecord>> DescribeEntityAsync(
        string? profileName, string entityLogicalName, bool includeSystem, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        var request = new RetrieveEntityRequest
        {
            LogicalName = entityLogicalName,
            EntityFilters = EntityFilters.Attributes,
            RetrieveAsIfPublished = true
        };

        var response = (RetrieveEntityResponse)
            await conn.Client.ExecuteAsync(request, ct).ConfigureAwait(false);

        var entityMeta = response.EntityMetadata;
        IEnumerable<AttributeMetadata> attributes = entityMeta.Attributes;

        // Filter out non-customizable system attributes unless explicitly requested.
        if (!includeSystem)
        {
            attributes = attributes.Where(a =>
                a.IsCustomAttribute == true || a.IsCustomizable?.Value == true);
        }

        return attributes
            .OrderBy(a => a.LogicalName, StringComparer.OrdinalIgnoreCase)
            .Select(a => new EntityAttributeRecord(
                LogicalName: a.LogicalName,
                SchemaName: a.SchemaName,
                DisplayName: a.DisplayName?.UserLocalizedLabel?.Label,
                AttributeTypeName: a.AttributeTypeName?.Value ?? a.AttributeType?.ToString() ?? "Unknown",
                IsCustomAttribute: a.IsCustomAttribute == true,
                IsPrimaryId: a.LogicalName == entityMeta.PrimaryIdAttribute,
                IsPrimaryName: a.LogicalName == entityMeta.PrimaryNameAttribute,
                MaxLength: a is StringAttributeMetadata strAttr ? strAttr.MaxLength : null,
                Description: a.Description?.UserLocalizedLabel?.Label))
            .ToList();
    }

    /// <inheritdoc />
    public async Task CreateAttributeAsync(
        string? profileName,
        string entityLogicalName,
        string schemaName,
        string displayName,
        string type,
        bool required,
        string? targetEntity,
        string[]? options,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        var requiredLevel = new AttributeRequiredLevelManagedProperty(
            required ? AttributeRequiredLevel.ApplicationRequired : AttributeRequiredLevel.None);

        switch (type.ToLowerInvariant())
        {
            case "lookup":
                if (string.IsNullOrWhiteSpace(targetEntity))
                    throw new ArgumentException("--target-entity is required for lookup attributes.");

                // Derive the relationship schema name from the referencing/referenced entities and column name.
                var columnSuffix = schemaName.Contains('_') ? schemaName[(schemaName.LastIndexOf('_') + 1)..] : schemaName;
                var prefix = schemaName.Contains('_') ? schemaName[..schemaName.IndexOf('_')] : schemaName;
                var relationshipSchemaName = $"{prefix}_{entityLogicalName}_{targetEntity}_{columnSuffix}";

                var lookupRequest = new CreateOneToManyRequest
                {
                    OneToManyRelationship = new OneToManyRelationshipMetadata
                    {
                        SchemaName = relationshipSchemaName,
                        ReferencedEntity = targetEntity,
                        ReferencingEntity = entityLogicalName,
                        CascadeConfiguration = new CascadeConfiguration
                        {
                            Assign = CascadeType.NoCascade,
                            Delete = CascadeType.RemoveLink,
                            Merge = CascadeType.NoCascade,
                            Reparent = CascadeType.NoCascade,
                            Share = CascadeType.NoCascade,
                            Unshare = CascadeType.NoCascade
                        }
                    },
                    Lookup = new LookupAttributeMetadata
                    {
                        SchemaName = schemaName,
                        DisplayName = new Label(displayName, 1033),
                        RequiredLevel = requiredLevel
                    }
                };
                await conn.Client.ExecuteAsync(lookupRequest, ct).ConfigureAwait(false);
                break;

            case "choice":
                CreatePicklistAttribute(conn, entityLogicalName, schemaName, displayName, requiredLevel, options, ct);
                break;

            case "multichoice":
                CreateMultiSelectPicklistAttribute(conn, entityLogicalName, schemaName, displayName, requiredLevel, options, ct);
                break;

            case "string":
                await conn.Client.ExecuteAsync(new CreateAttributeRequest
                {
                    EntityName = entityLogicalName,
                    Attribute = new StringAttributeMetadata
                    {
                        SchemaName = schemaName,
                        DisplayName = new Label(displayName, 1033),
                        RequiredLevel = requiredLevel,
                        MaxLength = 200
                    }
                }, ct).ConfigureAwait(false);
                break;

            case "number":
                await conn.Client.ExecuteAsync(new CreateAttributeRequest
                {
                    EntityName = entityLogicalName,
                    Attribute = new IntegerAttributeMetadata
                    {
                        SchemaName = schemaName,
                        DisplayName = new Label(displayName, 1033),
                        RequiredLevel = requiredLevel
                    }
                }, ct).ConfigureAwait(false);
                break;

            case "money":
                await conn.Client.ExecuteAsync(new CreateAttributeRequest
                {
                    EntityName = entityLogicalName,
                    Attribute = new MoneyAttributeMetadata
                    {
                        SchemaName = schemaName,
                        DisplayName = new Label(displayName, 1033),
                        RequiredLevel = requiredLevel
                    }
                }, ct).ConfigureAwait(false);
                break;

            case "bool":
                await conn.Client.ExecuteAsync(new CreateAttributeRequest
                {
                    EntityName = entityLogicalName,
                    Attribute = new BooleanAttributeMetadata
                    {
                        SchemaName = schemaName,
                        DisplayName = new Label(displayName, 1033),
                        RequiredLevel = requiredLevel,
                        OptionSet = new BooleanOptionSetMetadata(
                            new OptionMetadata(new Label("Yes", 1033), 1),
                            new OptionMetadata(new Label("No", 1033), 0))
                    }
                }, ct).ConfigureAwait(false);
                break;

            case "datetime":
                await conn.Client.ExecuteAsync(new CreateAttributeRequest
                {
                    EntityName = entityLogicalName,
                    Attribute = new DateTimeAttributeMetadata
                    {
                        SchemaName = schemaName,
                        DisplayName = new Label(displayName, 1033),
                        RequiredLevel = requiredLevel,
                        Format = DateTimeFormat.DateAndTime
                    }
                }, ct).ConfigureAwait(false);
                break;

            case "decimal":
                await conn.Client.ExecuteAsync(new CreateAttributeRequest
                {
                    EntityName = entityLogicalName,
                    Attribute = new DecimalAttributeMetadata
                    {
                        SchemaName = schemaName,
                        DisplayName = new Label(displayName, 1033),
                        RequiredLevel = requiredLevel
                    }
                }, ct).ConfigureAwait(false);
                break;

            case "float":
                await conn.Client.ExecuteAsync(new CreateAttributeRequest
                {
                    EntityName = entityLogicalName,
                    Attribute = new DoubleAttributeMetadata
                    {
                        SchemaName = schemaName,
                        DisplayName = new Label(displayName, 1033),
                        RequiredLevel = requiredLevel
                    }
                }, ct).ConfigureAwait(false);
                break;

            case "image":
                await conn.Client.ExecuteAsync(new CreateAttributeRequest
                {
                    EntityName = entityLogicalName,
                    Attribute = new ImageAttributeMetadata
                    {
                        SchemaName = schemaName,
                        DisplayName = new Label(displayName, 1033),
                        RequiredLevel = requiredLevel
                    }
                }, ct).ConfigureAwait(false);
                break;

            case "file":
                await conn.Client.ExecuteAsync(new CreateAttributeRequest
                {
                    EntityName = entityLogicalName,
                    Attribute = new FileAttributeMetadata
                    {
                        SchemaName = schemaName,
                        DisplayName = new Label(displayName, 1033),
                        RequiredLevel = requiredLevel,
                        MaxSizeInKB = 131072 // 128 MB
                    }
                }, ct).ConfigureAwait(false);
                break;

            default:
                throw new NotSupportedException($"Attribute type '{type}' is not supported. " +
                    "Supported types: lookup, choice, multichoice, string, number, money, bool, datetime, decimal, float, image, file.");
        }

        // Publish the entity so the new attribute is visible.
        await PublishEntityAsync(conn, entityLogicalName, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CreateManyToManyRelationshipAsync(
        string? profileName,
        string entity1,
        string entity2,
        string schemaName,
        string? displayName,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        var request = new CreateManyToManyRequest
        {
            IntersectEntitySchemaName = schemaName,
            ManyToManyRelationship = new ManyToManyRelationshipMetadata
            {
                SchemaName = schemaName,
                Entity1LogicalName = entity1,
                Entity2LogicalName = entity2,
                Entity1AssociatedMenuConfiguration = new AssociatedMenuConfiguration
                {
                    Behavior = AssociatedMenuBehavior.UseLabel,
                    Group = AssociatedMenuGroup.Details,
                    Label = new Label(displayName ?? entity2, 1033),
                    Order = 10000
                },
                Entity2AssociatedMenuConfiguration = new AssociatedMenuConfiguration
                {
                    Behavior = AssociatedMenuBehavior.UseLabel,
                    Group = AssociatedMenuGroup.Details,
                    Label = new Label(displayName ?? entity1, 1033),
                    Order = 10000
                }
            }
        };

        await conn.Client.ExecuteAsync(request, ct).ConfigureAwait(false);

        // Publish both entities so the relationship is visible.
        await PublishEntityAsync(conn, entity1, ct).ConfigureAwait(false);
        await PublishEntityAsync(conn, entity2, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EntityRelationshipRecord>> ListRelationshipsAsync(
        string? profileName,
        string entityLogicalName,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        var request = new RetrieveEntityRequest
        {
            LogicalName = entityLogicalName,
            EntityFilters = EntityFilters.Relationships,
            RetrieveAsIfPublished = true
        };

        var response = (RetrieveEntityResponse)
            await conn.Client.ExecuteAsync(request, ct).ConfigureAwait(false);

        var entityMeta = response.EntityMetadata;
        var results = new List<EntityRelationshipRecord>();

        // One-to-many relationships (this entity is the referenced/parent side).
        foreach (var rel in entityMeta.OneToManyRelationships ?? [])
        {
            results.Add(new EntityRelationshipRecord(
                SchemaName: rel.SchemaName,
                RelationshipType: "OneToMany",
                Entity1LogicalName: rel.ReferencedEntity,
                Entity2LogicalName: rel.ReferencingEntity,
                IsCustomRelationship: rel.IsCustomRelationship == true,
                IntersectEntityName: null));
        }

        // Many-to-one relationships (this entity is the referencing/child side).
        foreach (var rel in entityMeta.ManyToOneRelationships ?? [])
        {
            results.Add(new EntityRelationshipRecord(
                SchemaName: rel.SchemaName,
                RelationshipType: "ManyToOne",
                Entity1LogicalName: rel.ReferencingEntity,
                Entity2LogicalName: rel.ReferencedEntity,
                IsCustomRelationship: rel.IsCustomRelationship == true,
                IntersectEntityName: null));
        }

        // Many-to-many relationships.
        foreach (var rel in entityMeta.ManyToManyRelationships ?? [])
        {
            results.Add(new EntityRelationshipRecord(
                SchemaName: rel.SchemaName,
                RelationshipType: "ManyToMany",
                Entity1LogicalName: rel.Entity1LogicalName,
                Entity2LogicalName: rel.Entity2LogicalName,
                IsCustomRelationship: rel.IsCustomRelationship == true,
                IntersectEntityName: rel.IntersectEntityName));
        }

        return results
            .OrderBy(r => r.SchemaName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Case-insensitive contains check that handles null values safely.</summary>
    private static bool Contains(string? value, string search) =>
        value is not null && value.Contains(search, StringComparison.OrdinalIgnoreCase);

    /// <summary>Publishes customizations for a single entity.</summary>
    private static async Task PublishEntityAsync(DataverseConnection conn, string entityLogicalName, CancellationToken ct)
    {
        await conn.Client.ExecuteAsync(new PublishXmlRequest
        {
            ParameterXml = $"<importexportxml><entities><entity>{entityLogicalName}</entity></entities></importexportxml>"
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Creates a local choice (picklist) attribute.</summary>
    private static void CreatePicklistAttribute(
        DataverseConnection conn, string entityLogicalName, string schemaName,
        string displayName, AttributeRequiredLevelManagedProperty requiredLevel,
        string[]? optionLabels, CancellationToken ct)
    {
        if (optionLabels is null || optionLabels.Length == 0)
            throw new ArgumentException("--options is required for choice attributes.");

        var optionSet = new OptionSetMetadata { IsGlobal = false, OptionSetType = OptionSetType.Picklist };
        int value = 100_000_000;
        foreach (var label in optionLabels)
            optionSet.Options.Add(new OptionMetadata(new Label(label.Trim(), 1033), value++));

        conn.Client.Execute(new CreateAttributeRequest
        {
            EntityName = entityLogicalName,
            Attribute = new PicklistAttributeMetadata
            {
                SchemaName = schemaName,
                DisplayName = new Label(displayName, 1033),
                RequiredLevel = requiredLevel,
                OptionSet = optionSet
            }
        });
    }

    /// <summary>Creates a local multi-select choice attribute.</summary>
    private static void CreateMultiSelectPicklistAttribute(
        DataverseConnection conn, string entityLogicalName, string schemaName,
        string displayName, AttributeRequiredLevelManagedProperty requiredLevel,
        string[]? optionLabels, CancellationToken ct)
    {
        if (optionLabels is null || optionLabels.Length == 0)
            throw new ArgumentException("--options is required for multichoice attributes.");

        var optionSet = new OptionSetMetadata { IsGlobal = false, OptionSetType = OptionSetType.Picklist };
        int value = 100_000_000;
        foreach (var label in optionLabels)
            optionSet.Options.Add(new OptionMetadata(new Label(label.Trim(), 1033), value++));

        conn.Client.Execute(new CreateAttributeRequest
        {
            EntityName = entityLogicalName,
            Attribute = new MultiSelectPicklistAttributeMetadata
            {
                SchemaName = schemaName,
                DisplayName = new Label(displayName, 1033),
                RequiredLevel = requiredLevel,
                OptionSet = optionSet
            }
        });
    }
}
