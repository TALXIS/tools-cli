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
        CreateAttributeOptions options,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        var requiredLevel = new AttributeRequiredLevelManagedProperty(
            MapRequiredLevel(options.RequiredLevel));

        var displayLabel = new Label(options.DisplayName ?? options.SchemaName, 1033);
        var descriptionLabel = options.Description is not null ? new Label(options.Description, 1033) : null;

        switch (options.Type)
        {
            case "string":
                await ExecuteCreateAttribute(conn, options, new StringAttributeMetadata
                {
                    SchemaName = options.SchemaName,
                    DisplayName = displayLabel,
                    Description = descriptionLabel,
                    RequiredLevel = requiredLevel,
                    MaxLength = options.MaxLength ?? 200,
                    FormatName = options.StringFormat is not null ? MapStringFormat(options.StringFormat) : StringFormatName.Text
                }, ct).ConfigureAwait(false);
                break;

            case "memo":
                await ExecuteCreateAttribute(conn, options, new MemoAttributeMetadata
                {
                    SchemaName = options.SchemaName,
                    DisplayName = displayLabel,
                    Description = descriptionLabel,
                    RequiredLevel = requiredLevel,
                    MaxLength = options.MaxLength ?? 2000
                }, ct).ConfigureAwait(false);
                break;

            case "number":
                var intMeta = new IntegerAttributeMetadata
                {
                    SchemaName = options.SchemaName,
                    DisplayName = displayLabel,
                    Description = descriptionLabel,
                    RequiredLevel = requiredLevel
                };
                if (options.MinValue.HasValue) intMeta.MinValue = (int)options.MinValue.Value;
                if (options.MaxValue.HasValue) intMeta.MaxValue = (int)options.MaxValue.Value;
                if (options.NumberFormat.HasValue()) intMeta.Format = MapIntegerFormat(options.NumberFormat!);
                await ExecuteCreateAttribute(conn, options, intMeta, ct).ConfigureAwait(false);
                break;

            case "decimal":
                var decMeta = new DecimalAttributeMetadata
                {
                    SchemaName = options.SchemaName,
                    DisplayName = displayLabel,
                    Description = descriptionLabel,
                    RequiredLevel = requiredLevel
                };
                if (options.MinValue.HasValue) decMeta.MinValue = (decimal)options.MinValue.Value;
                if (options.MaxValue.HasValue) decMeta.MaxValue = (decimal)options.MaxValue.Value;
                if (options.Precision.HasValue) decMeta.Precision = options.Precision.Value;
                await ExecuteCreateAttribute(conn, options, decMeta, ct).ConfigureAwait(false);
                break;

            case "float":
                var dblMeta = new DoubleAttributeMetadata
                {
                    SchemaName = options.SchemaName,
                    DisplayName = displayLabel,
                    Description = descriptionLabel,
                    RequiredLevel = requiredLevel
                };
                if (options.MinValue.HasValue) dblMeta.MinValue = options.MinValue.Value;
                if (options.MaxValue.HasValue) dblMeta.MaxValue = options.MaxValue.Value;
                if (options.Precision.HasValue) dblMeta.Precision = options.Precision.Value;
                await ExecuteCreateAttribute(conn, options, dblMeta, ct).ConfigureAwait(false);
                break;

            case "money":
                var moneyMeta = new MoneyAttributeMetadata
                {
                    SchemaName = options.SchemaName,
                    DisplayName = displayLabel,
                    Description = descriptionLabel,
                    RequiredLevel = requiredLevel
                };
                if (options.MinValue.HasValue) moneyMeta.MinValue = options.MinValue.Value;
                if (options.MaxValue.HasValue) moneyMeta.MaxValue = options.MaxValue.Value;
                if (options.Precision.HasValue) moneyMeta.Precision = options.Precision.Value;
                if (options.PrecisionSource.HasValue) moneyMeta.PrecisionSource = options.PrecisionSource.Value;
                await ExecuteCreateAttribute(conn, options, moneyMeta, ct).ConfigureAwait(false);
                break;

            case "bool":
                await ExecuteCreateAttribute(conn, options, new BooleanAttributeMetadata
                {
                    SchemaName = options.SchemaName,
                    DisplayName = displayLabel,
                    Description = descriptionLabel,
                    RequiredLevel = requiredLevel,
                    OptionSet = new BooleanOptionSetMetadata(
                        new OptionMetadata(new Label(options.TrueLabel, 1033), 1),
                        new OptionMetadata(new Label(options.FalseLabel, 1033), 0))
                }, ct).ConfigureAwait(false);
                break;

            case "datetime":
                var dtMeta = new DateTimeAttributeMetadata
                {
                    SchemaName = options.SchemaName,
                    DisplayName = displayLabel,
                    Description = descriptionLabel,
                    RequiredLevel = requiredLevel,
                    Format = options.DateTimeFormat is not null ? MapDateTimeFormat(options.DateTimeFormat) : DateTimeFormat.DateAndTime
                };
                if (options.DateTimeBehavior is not null)
                    dtMeta.DateTimeBehavior = MapDateTimeBehavior(options.DateTimeBehavior);
                await ExecuteCreateAttribute(conn, options, dtMeta, ct).ConfigureAwait(false);
                break;

            case "choice":
                await CreatePicklistAttribute(conn, options, displayLabel, descriptionLabel, requiredLevel, ct).ConfigureAwait(false);
                break;

            case "multichoice":
                await CreateMultiSelectPicklistAttribute(conn, options, displayLabel, descriptionLabel, requiredLevel, ct).ConfigureAwait(false);
                break;

            case "lookup":
                await CreateLookupAttribute(conn, options, displayLabel, requiredLevel, ct).ConfigureAwait(false);
                break;

            case "polymorphiclookup":
                await CreatePolymorphicLookupAttribute(conn, options, displayLabel, requiredLevel, ct).ConfigureAwait(false);
                break;

            case "customer":
                await CreateCustomerAttribute(conn, options, displayLabel, requiredLevel, ct).ConfigureAwait(false);
                break;

            case "image":
                var imgMeta = new ImageAttributeMetadata
                {
                    SchemaName = options.SchemaName,
                    DisplayName = displayLabel,
                    Description = descriptionLabel,
                    RequiredLevel = requiredLevel,
                    CanStoreFullImage = options.CanStoreFullImage
                };
                if (options.MaxSizeKb.HasValue) imgMeta.MaxSizeInKB = options.MaxSizeKb.Value;
                await ExecuteCreateAttribute(conn, options, imgMeta, ct).ConfigureAwait(false);
                break;

            case "file":
                await ExecuteCreateAttribute(conn, options, new FileAttributeMetadata
                {
                    SchemaName = options.SchemaName,
                    DisplayName = displayLabel,
                    Description = descriptionLabel,
                    RequiredLevel = requiredLevel,
                    MaxSizeInKB = options.MaxSizeKb ?? 131072 // 128 MB default
                }, ct).ConfigureAwait(false);
                break;

            default:
                throw new NotSupportedException($"Attribute type '{options.Type}' is not supported.");
        }

        // Publish the entity so the new attribute is visible.
        await PublishEntityAsync(conn, options.EntityLogicalName, ct).ConfigureAwait(false);
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

    /// <inheritdoc />
    public async Task CreateEntityAsync(
        string? profileName,
        string schemaName,
        string displayName,
        string pluralName,
        string? description,
        string? solution,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        var entity = new EntityMetadata
        {
            SchemaName = schemaName,
            DisplayName = new Label(displayName, 1033),
            DisplayCollectionName = new Label(pluralName, 1033),
            Description = description != null ? new Label(description, 1033) : null,
            OwnershipType = OwnershipTypes.UserOwned,
            IsActivity = false
        };

        // Primary name attribute is required when creating an entity.
        var prefix = schemaName.Contains('_') ? schemaName[..schemaName.IndexOf('_')] : schemaName;
        var primaryAttribute = new StringAttributeMetadata
        {
            SchemaName = $"{prefix}_name",
            MaxLength = 200,
            RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired),
            DisplayName = new Label("Name", 1033)
        };

        var request = new CreateEntityRequest
        {
            Entity = entity,
            PrimaryAttribute = primaryAttribute
        };

        if (!string.IsNullOrEmpty(solution))
            request["SolutionUniqueName"] = solution;

        await conn.Client.ExecuteAsync(request, ct).ConfigureAwait(false);

        await PublishEntityAsync(conn, schemaName.ToLowerInvariant(), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteEntityAsync(
        string? profileName,
        string entityLogicalName,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        await conn.Client.ExecuteAsync(new DeleteEntityRequest
        {
            LogicalName = entityLogicalName
        }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateAttributeAsync(
        string? profileName,
        string entityLogicalName,
        string attributeLogicalName,
        string? displayName,
        string? description,
        string? requiredLevel,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        // Retrieve the published attribute metadata (not as-if-published) so
        // we base our update on what Dataverse currently has active.
        var retrieveResponse = (RetrieveAttributeResponse)await conn.Client.ExecuteAsync(
            new RetrieveAttributeRequest
            {
                EntityLogicalName = entityLogicalName,
                LogicalName = attributeLogicalName,
                RetrieveAsIfPublished = false
            }, ct).ConfigureAwait(false);

        var attribute = retrieveResponse.AttributeMetadata;

        if (displayName is not null)
            attribute.DisplayName = new Label(displayName, 1033);

        if (description is not null)
            attribute.Description = new Label(description, 1033);

        if (requiredLevel is not null)
        {
            var level = requiredLevel.ToLowerInvariant() switch
            {
                "none" => AttributeRequiredLevel.None,
                "recommended" => AttributeRequiredLevel.Recommended,
                "required" => AttributeRequiredLevel.ApplicationRequired,
                _ => throw new ArgumentException($"Invalid required level '{requiredLevel}'. Use: none, recommended, required.")
            };
            // Modify the existing managed property's Value rather than replacing
            // the entire object — preserves CanBeChanged and other flags.
            if (attribute.RequiredLevel is not null)
            {
                attribute.RequiredLevel.Value = level;
            }
            else
            {
                attribute.RequiredLevel = new AttributeRequiredLevelManagedProperty(level);
            }
        }

        conn.Client.ForceServerMetadataCacheConsistency = true;

        await conn.Client.ExecuteAsync(new UpdateAttributeRequest
        {
            EntityName = entityLogicalName,
            Attribute = attribute,
            MergeLabels = false
        }, ct).ConfigureAwait(false);

        await PublishEntityAsync(conn, entityLogicalName, ct).ConfigureAwait(false);

        // Verify the update was applied
        var verifyResp = (RetrieveAttributeResponse)await conn.Client.ExecuteAsync(
            new RetrieveAttributeRequest
            {
                EntityLogicalName = entityLogicalName,
                LogicalName = attributeLogicalName,
                RetrieveAsIfPublished = false
            }, ct).ConfigureAwait(false);

        if (requiredLevel is not null)
        {
            var expected = requiredLevel.ToLowerInvariant() switch
            {
                "required" => "ApplicationRequired",
                "recommended" => "Recommended",
                _ => "None"
            };
            var actual = verifyResp.AttributeMetadata.RequiredLevel?.Value.ToString();
            if (actual != expected)
            {
                // SDK UpdateAttributeRequest is known to silently drop
                // RequiredLevel changes. Warn the user.
                throw new InvalidOperationException(
                    $"RequiredLevel change to '{requiredLevel}' was not applied by the server. " +
                    $"Current value is still '{actual}'. This is a known SDK limitation — " +
                    $"use the Power Apps maker portal (make.powerapps.com) to change the requirement level.");
            }
        }
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, object?>> GetAttributeDetailAsync(
        string? profileName,
        string entityLogicalName,
        string attributeLogicalName,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        // Force the server to refresh its metadata cache so that recently
        // published changes (e.g. RequiredLevel updates) are visible immediately.
        conn.Client.ForceServerMetadataCacheConsistency = true;

        var request = new RetrieveAttributeRequest
        {
            EntityLogicalName = entityLogicalName,
            LogicalName = attributeLogicalName,
            RetrieveAsIfPublished = false
        };

        var response = (RetrieveAttributeResponse)
            await conn.Client.ExecuteAsync(request, ct).ConfigureAwait(false);

        var attr = response.AttributeMetadata;
        return BuildAttributeDetail(attr);
    }

    /// <summary>
    /// Builds a detail dictionary from <see cref="AttributeMetadata"/>, including
    /// common fields and type-specific properties based on the concrete subclass.
    /// </summary>
    private static Dictionary<string, object?> BuildAttributeDetail(AttributeMetadata attr)
    {
        var detail = new Dictionary<string, object?>
        {
            ["Logical Name"] = attr.LogicalName,
            ["Schema Name"] = attr.SchemaName,
            ["Display Name"] = attr.DisplayName?.UserLocalizedLabel?.Label ?? "-",
            ["Description"] = attr.Description?.UserLocalizedLabel?.Label ?? "-",
            ["Attribute Type"] = attr.AttributeTypeName?.Value ?? attr.AttributeType?.ToString() ?? "Unknown",
            ["Required Level"] = attr.RequiredLevel?.Value.ToString() ?? "-",
            ["Required Level Can Change"] = attr.RequiredLevel?.CanBeChanged,
            ["Is Custom"] = attr.IsCustomAttribute == true,
            ["Is Auditable"] = attr.IsAuditEnabled?.Value == true,
        };

        switch (attr)
        {
            case StringAttributeMetadata s:
                detail["MaxLength"] = s.MaxLength;
                detail["FormatName"] = s.FormatName?.Value;
                break;
            case MemoAttributeMetadata m:
                detail["MaxLength"] = m.MaxLength;
                break;
            case IntegerAttributeMetadata i:
                detail["MinValue"] = i.MinValue;
                detail["MaxValue"] = i.MaxValue;
                detail["Format"] = i.Format?.ToString();
                break;
            case DecimalAttributeMetadata d:
                detail["MinValue"] = d.MinValue;
                detail["MaxValue"] = d.MaxValue;
                detail["Precision"] = d.Precision;
                break;
            case DoubleAttributeMetadata f:
                detail["MinValue"] = f.MinValue;
                detail["MaxValue"] = f.MaxValue;
                detail["Precision"] = f.Precision;
                break;
            case MoneyAttributeMetadata mo:
                detail["MinValue"] = mo.MinValue;
                detail["MaxValue"] = mo.MaxValue;
                detail["Precision"] = mo.Precision;
                detail["PrecisionSource"] = mo.PrecisionSource;
                break;
            case BooleanAttributeMetadata b:
                detail["TrueOption"] = b.OptionSet?.TrueOption?.Label?.UserLocalizedLabel?.Label;
                detail["FalseOption"] = b.OptionSet?.FalseOption?.Label?.UserLocalizedLabel?.Label;
                break;
            case DateTimeAttributeMetadata dt:
                detail["Format"] = dt.Format?.ToString();
                detail["DateTimeBehavior"] = dt.DateTimeBehavior?.Value;
                break;
            case PicklistAttributeMetadata p:
                detail["Is Global Option Set"] = p.OptionSet?.IsGlobal;
                detail["Option Set Name"] = p.OptionSet?.Name;
                detail["Options"] = p.OptionSet?.Options?.Select(o => new Dictionary<string, object?>
                {
                    ["Label"] = o.Label?.UserLocalizedLabel?.Label,
                    ["Value"] = o.Value
                }).ToList<object?>();
                break;
            case MultiSelectPicklistAttributeMetadata ms:
                detail["Is Global Option Set"] = ms.OptionSet?.IsGlobal;
                detail["Option Set Name"] = ms.OptionSet?.Name;
                detail["Options"] = ms.OptionSet?.Options?.Select(o => new Dictionary<string, object?>
                {
                    ["Label"] = o.Label?.UserLocalizedLabel?.Label,
                    ["Value"] = o.Value
                }).ToList<object?>();
                break;
            case LookupAttributeMetadata l:
                detail["Targets"] = l.Targets;
                break;
            case ImageAttributeMetadata im:
                detail["MaxSizeInKB"] = im.MaxSizeInKB;
                detail["CanStoreFullImage"] = im.CanStoreFullImage;
                break;
            case FileAttributeMetadata fi:
                detail["MaxSizeInKB"] = fi.MaxSizeInKB;
                break;
        }

        return detail;
    }

    /// <inheritdoc />
    public async Task DeleteAttributeAsync(
        string? profileName,
        string entityLogicalName,
        string attributeLogicalName,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        await conn.Client.ExecuteAsync(new DeleteAttributeRequest
        {
            EntityLogicalName = entityLogicalName,
            LogicalName = attributeLogicalName
        }, ct).ConfigureAwait(false);

        await PublishEntityAsync(conn, entityLogicalName, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteRelationshipAsync(
        string? profileName,
        string relationshipSchemaName,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        await conn.Client.ExecuteAsync(new DeleteRelationshipRequest
        {
            Name = relationshipSchemaName
        }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<EntityDetailRecord> GetEntityDetailAsync(
        string? profileName, string entityLogicalName, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        var request = new RetrieveEntityRequest
        {
            LogicalName = entityLogicalName,
            EntityFilters = EntityFilters.Entity,
            RetrieveAsIfPublished = true
        };

        var response = (RetrieveEntityResponse)
            await conn.Client.ExecuteAsync(request, ct).ConfigureAwait(false);

        var e = response.EntityMetadata;

        return new EntityDetailRecord(
            LogicalName: e.LogicalName,
            SchemaName: e.SchemaName,
            DisplayName: e.DisplayName?.UserLocalizedLabel?.Label,
            PluralDisplayName: e.DisplayCollectionName?.UserLocalizedLabel?.Label,
            Description: e.Description?.UserLocalizedLabel?.Label,
            EntityTypeCode: e.ObjectTypeCode,
            OwnershipType: e.OwnershipType?.ToString() ?? "Unknown",
            PrimaryIdAttribute: e.PrimaryIdAttribute,
            PrimaryNameAttribute: e.PrimaryNameAttribute,
            IsCustomEntity: e.IsCustomEntity == true,
            IsActivity: e.IsActivity == true,
            IsAuditEnabled: e.IsAuditEnabled?.Value == true,
            ChangeTrackingEnabled: e.ChangeTrackingEnabled == true,
            EntitySetName: e.EntitySetName,
            CollectionSchemaName: e.CollectionSchemaName,
            IsCustomizable: e.IsCustomizable?.Value == true);
    }

    /// <inheritdoc />
    public async Task UpdateEntityAsync(
        string? profileName, string entityLogicalName,
        string? displayName, string? pluralName, string? description,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        // Retrieve current metadata so we patch on top of existing values.
        var retrieveRequest = new RetrieveEntityRequest
        {
            LogicalName = entityLogicalName,
            EntityFilters = EntityFilters.Entity
        };
        var response = (RetrieveEntityResponse)
            await conn.Client.ExecuteAsync(retrieveRequest, ct).ConfigureAwait(false);
        var entityMeta = response.EntityMetadata;

        // Update only the fields that were explicitly provided.
        if (displayName is not null)
            entityMeta.DisplayName = new Label(displayName, 1033);
        if (pluralName is not null)
            entityMeta.DisplayCollectionName = new Label(pluralName, 1033);
        if (description is not null)
            entityMeta.Description = new Label(description, 1033);

        await conn.Client.ExecuteAsync(new UpdateEntityRequest { Entity = entityMeta }, ct).ConfigureAwait(false);
        await PublishEntityAsync(conn, entityLogicalName, ct).ConfigureAwait(false);
    }

    // ===== Private helpers =====

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

    /// <summary>Executes a <see cref="CreateAttributeRequest"/>, optionally targeting a solution.</summary>
    private static async Task ExecuteCreateAttribute(
        DataverseConnection conn, CreateAttributeOptions options, AttributeMetadata attribute, CancellationToken ct)
    {
        var request = new CreateAttributeRequest
        {
            EntityName = options.EntityLogicalName,
            Attribute = attribute
        };

        if (!string.IsNullOrWhiteSpace(options.SolutionUniqueName))
            request.Parameters["SolutionUniqueName"] = options.SolutionUniqueName;

        await conn.Client.ExecuteAsync(request, ct).ConfigureAwait(false);
    }

    /// <summary>Creates a local or global-linked choice (picklist) attribute.</summary>
    private static async Task CreatePicklistAttribute(
        DataverseConnection conn, CreateAttributeOptions options, Label displayLabel,
        Label? descriptionLabel, AttributeRequiredLevelManagedProperty requiredLevel, CancellationToken ct)
    {
        var picklistMeta = new PicklistAttributeMetadata
        {
            SchemaName = options.SchemaName,
            DisplayName = displayLabel,
            Description = descriptionLabel,
            RequiredLevel = requiredLevel
        };

        if (!string.IsNullOrWhiteSpace(options.GlobalOptionSetName))
        {
            // Reference an existing global option set.
            picklistMeta.OptionSet = new OptionSetMetadata { IsGlobal = true, Name = options.GlobalOptionSetName };
        }
        else
        {
            picklistMeta.OptionSet = BuildLocalOptionSet(options);
        }

        await ExecuteCreateAttribute(conn, options, picklistMeta, ct).ConfigureAwait(false);
    }

    /// <summary>Creates a local or global-linked multi-select choice attribute.</summary>
    private static async Task CreateMultiSelectPicklistAttribute(
        DataverseConnection conn, CreateAttributeOptions options, Label displayLabel,
        Label? descriptionLabel, AttributeRequiredLevelManagedProperty requiredLevel, CancellationToken ct)
    {
        var multiMeta = new MultiSelectPicklistAttributeMetadata
        {
            SchemaName = options.SchemaName,
            DisplayName = displayLabel,
            Description = descriptionLabel,
            RequiredLevel = requiredLevel
        };

        if (!string.IsNullOrWhiteSpace(options.GlobalOptionSetName))
        {
            multiMeta.OptionSet = new OptionSetMetadata { IsGlobal = true, Name = options.GlobalOptionSetName };
        }
        else
        {
            multiMeta.OptionSet = BuildLocalOptionSet(options);
        }

        await ExecuteCreateAttribute(conn, options, multiMeta, ct).ConfigureAwait(false);
    }

    /// <summary>Builds a local (non-global) option set from the parsed option tuples.</summary>
    private static OptionSetMetadata BuildLocalOptionSet(CreateAttributeOptions options)
    {
        if (options.Options is null || options.Options.Length == 0)
            throw new ArgumentException("--options is required when not using --global-optionset.");

        var optionSet = new OptionSetMetadata { IsGlobal = false, OptionSetType = OptionSetType.Picklist };
        foreach (var (label, value) in options.Options)
            optionSet.Options.Add(new OptionMetadata(new Label(label, 1033), value));

        return optionSet;
    }

    /// <summary>Creates a standard 1:N lookup attribute.</summary>
    private static async Task CreateLookupAttribute(
        DataverseConnection conn, CreateAttributeOptions options, Label displayLabel,
        AttributeRequiredLevelManagedProperty requiredLevel, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.TargetEntity))
            throw new ArgumentException("TargetEntity is required for lookup attributes.");

        var cascadeDelete = MapCascadeType(options.CascadeDelete);

        // Derive the relationship schema name from the referencing/referenced entities and column name.
        var columnSuffix = options.SchemaName.Contains('_') ? options.SchemaName[(options.SchemaName.LastIndexOf('_') + 1)..] : options.SchemaName;
        var prefix = options.SchemaName.Contains('_') ? options.SchemaName[..options.SchemaName.IndexOf('_')] : options.SchemaName;
        var relationshipSchemaName = $"{prefix}_{options.EntityLogicalName}_{options.TargetEntity}_{columnSuffix}";

        var lookupRequest = new CreateOneToManyRequest
        {
            OneToManyRelationship = new OneToManyRelationshipMetadata
            {
                SchemaName = relationshipSchemaName,
                ReferencedEntity = options.TargetEntity,
                ReferencingEntity = options.EntityLogicalName,
                CascadeConfiguration = new CascadeConfiguration
                {
                    Assign = CascadeType.NoCascade,
                    Delete = cascadeDelete,
                    Merge = CascadeType.NoCascade,
                    Reparent = CascadeType.NoCascade,
                    Share = CascadeType.NoCascade,
                    Unshare = CascadeType.NoCascade
                }
            },
            Lookup = new LookupAttributeMetadata
            {
                SchemaName = options.SchemaName,
                DisplayName = displayLabel,
                RequiredLevel = requiredLevel
            }
        };

        if (!string.IsNullOrWhiteSpace(options.SolutionUniqueName))
            lookupRequest.Parameters["SolutionUniqueName"] = options.SolutionUniqueName;

        await conn.Client.ExecuteAsync(lookupRequest, ct).ConfigureAwait(false);
    }

    /// <summary>Creates a polymorphic lookup attribute targeting multiple entities.</summary>
    private static async Task CreatePolymorphicLookupAttribute(
        DataverseConnection conn, CreateAttributeOptions options, Label displayLabel,
        AttributeRequiredLevelManagedProperty requiredLevel, CancellationToken ct)
    {
        if (options.TargetEntities is null || options.TargetEntities.Length == 0)
            throw new ArgumentException("TargetEntities is required for polymorphic lookup attributes.");

        var cascadeDelete = MapCascadeType(options.CascadeDelete);
        var prefix = options.SchemaName.Contains('_') ? options.SchemaName[..options.SchemaName.IndexOf('_')] : options.SchemaName;
        var columnSuffix = options.SchemaName.Contains('_') ? options.SchemaName[(options.SchemaName.LastIndexOf('_') + 1)..] : options.SchemaName;

        var relationships = options.TargetEntities.Select(target => new OneToManyRelationshipMetadata
        {
            SchemaName = $"{prefix}_{options.EntityLogicalName}_{target.Trim()}_{columnSuffix}",
            ReferencedEntity = target.Trim(),
            ReferencingEntity = options.EntityLogicalName,
            CascadeConfiguration = new CascadeConfiguration
            {
                Assign = CascadeType.NoCascade,
                Delete = cascadeDelete,
                Merge = CascadeType.NoCascade,
                Reparent = CascadeType.NoCascade,
                Share = CascadeType.NoCascade,
                Unshare = CascadeType.NoCascade
            }
        }).ToArray();

        var polyRequest = new CreatePolymorphicLookupAttributeRequest
        {
            Lookup = new LookupAttributeMetadata
            {
                SchemaName = options.SchemaName,
                DisplayName = displayLabel,
                RequiredLevel = requiredLevel
            },
            OneToManyRelationships = relationships
        };

        if (!string.IsNullOrWhiteSpace(options.SolutionUniqueName))
            polyRequest.Parameters["SolutionUniqueName"] = options.SolutionUniqueName;

        await conn.Client.ExecuteAsync(polyRequest, ct).ConfigureAwait(false);
    }

    /// <summary>Creates a customer lookup attribute (targets account + contact).</summary>
    private static async Task CreateCustomerAttribute(
        DataverseConnection conn, CreateAttributeOptions options, Label displayLabel,
        AttributeRequiredLevelManagedProperty requiredLevel, CancellationToken ct)
    {
        var cascadeDelete = MapCascadeType(options.CascadeDelete);
        var prefix = options.SchemaName.Contains('_') ? options.SchemaName[..options.SchemaName.IndexOf('_')] : options.SchemaName;
        var columnSuffix = options.SchemaName.Contains('_') ? options.SchemaName[(options.SchemaName.LastIndexOf('_') + 1)..] : options.SchemaName;

        var customerRequest = new CreateCustomerRelationshipsRequest
        {
            Lookup = new LookupAttributeMetadata
            {
                SchemaName = options.SchemaName,
                DisplayName = displayLabel,
                RequiredLevel = requiredLevel
            },
            OneToManyRelationships = new[]
            {
                new OneToManyRelationshipMetadata
                {
                    SchemaName = $"{prefix}_{options.EntityLogicalName}_account_{columnSuffix}",
                    ReferencedEntity = "account",
                    ReferencingEntity = options.EntityLogicalName,
                    CascadeConfiguration = new CascadeConfiguration
                    {
                        Assign = CascadeType.NoCascade,
                        Delete = cascadeDelete,
                        Merge = CascadeType.NoCascade,
                        Reparent = CascadeType.NoCascade,
                        Share = CascadeType.NoCascade,
                        Unshare = CascadeType.NoCascade
                    }
                },
                new OneToManyRelationshipMetadata
                {
                    SchemaName = $"{prefix}_{options.EntityLogicalName}_contact_{columnSuffix}",
                    ReferencedEntity = "contact",
                    ReferencingEntity = options.EntityLogicalName,
                    CascadeConfiguration = new CascadeConfiguration
                    {
                        Assign = CascadeType.NoCascade,
                        Delete = cascadeDelete,
                        Merge = CascadeType.NoCascade,
                        Reparent = CascadeType.NoCascade,
                        Share = CascadeType.NoCascade,
                        Unshare = CascadeType.NoCascade
                    }
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(options.SolutionUniqueName))
            customerRequest.Parameters["SolutionUniqueName"] = options.SolutionUniqueName;

        await conn.Client.ExecuteAsync(customerRequest, ct).ConfigureAwait(false);
    }

    // ===== Mapping helpers =====

    private static AttributeRequiredLevel MapRequiredLevel(string value) => value switch
    {
        "recommended" => AttributeRequiredLevel.Recommended,
        "required" => AttributeRequiredLevel.ApplicationRequired,
        _ => AttributeRequiredLevel.None
    };

    private static StringFormatName MapStringFormat(string value) => value switch
    {
        "email" => StringFormatName.Email,
        "url" => StringFormatName.Url,
        "phone" => StringFormatName.Phone,
        "textarea" => StringFormatName.TextArea,
        "tickersymbol" => StringFormatName.TickerSymbol,
        _ => StringFormatName.Text
    };

    private static IntegerFormat MapIntegerFormat(string value) => value switch
    {
        "duration" => IntegerFormat.Duration,
        "timezone" => IntegerFormat.TimeZone,
        "language" => IntegerFormat.Language,
        "locale" => IntegerFormat.Locale,
        _ => IntegerFormat.None
    };

    private static DateTimeFormat MapDateTimeFormat(string value) => value switch
    {
        "dateonly" => DateTimeFormat.DateOnly,
        _ => DateTimeFormat.DateAndTime
    };

    private static DateTimeBehavior MapDateTimeBehavior(string value) => value switch
    {
        "dateonly" => DateTimeBehavior.DateOnly,
        "timezoneindependent" => DateTimeBehavior.TimeZoneIndependent,
        _ => DateTimeBehavior.UserLocal
    };

    private static CascadeType MapCascadeType(string value) => value switch
    {
        "cascade" => CascadeType.Cascade,
        "restrict" => CascadeType.Restrict,
        _ => CascadeType.RemoveLink
    };
}

/// <summary>Extension to check non-null-or-whitespace on nullable strings.</summary>
internal static class StringExtensions
{
    public static bool HasValue(this string? value) => !string.IsNullOrWhiteSpace(value);
}
