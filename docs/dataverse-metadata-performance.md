# Dataverse Metadata Mutation Performance Guide

## Introduction

Schema operations in Microsoft Dataverse — creating or updating entities, attributes, and relationships — can be surprisingly slow. A single entity creation may take 30–120 seconds, and publishing customizations acquires an organization-wide exclusive lock. When you multiply these costs across dozens of tables and hundreds of columns, automation pipelines can grind to a halt.

This guide documents observed behaviors and optimization strategies for Dataverse metadata mutations. It is relevant for CLI tools, CI/CD pipelines, AI agents, and any automation that modifies Dataverse schema programmatically.

All behaviors described here were observed through standard API usage, network analysis, and official Microsoft documentation.

---

## 1. Entity Creation

### Standard Approach: `CreateEntityRequest`

Creates one entity at a time via the Organization Service SDK.

Each call triggers a chain of server-side operations:
- Table DDL (SQL table creation)
- Default views and forms creation
- Metadata cache refresh
- Auto-publish of the initial entity components (forms, views, sitemap entry)

**Typical duration**: 30–120 seconds per entity, depending on environment load and entity complexity.

After creation, any *subsequent* modifications (adding columns, changing forms) require an explicit `PublishXmlRequest` to go live.

### Batch Approach: `CreateEntities` Action

> **CLI usage:** `txc env entity create` uses this API internally when creating entities. When multiple entity creations are staged in a changeset, `txc env changeset apply` batches them via `ExecuteMultiple` with inline attributes in Phase 1a of the [changeset apply pipeline](changeset-staging.md#the-4-phase-apply-pipeline).

Available as an Organization Service message:

```csharp
var request = new OrganizationRequest("CreateEntities");
request["Entities"] = entityMetadataArray; // EntityMetadata[]
request["SolutionUniqueName"] = "MySolution";
```

Key characteristics:
- Accepts an array of `EntityMetadata[]` with attributes and relationships defined inline
- Creates multiple entities in a single server-side transaction
- Suppresses intermediate cache refreshes and batches DDL operations
- **Typical duration**: 12–20 seconds for 3–4 entities with all attributes and relationships
- Returns `EntityIds` (`Guid[]`) on success
- Supports `SolutionUniqueName` parameter for solution-aware creation
- Supports scenario headers: `MSCRM.CreateEntities.ScenarioName`, `MSCRM.CreateEntities.RetryCount`

> **Note**: This message is only available via the SDK (Organization Service). It is **not** exposed as a Web API action endpoint.

This message is used by Power Apps maker experiences such as Data Workspace and Vibe Coding, and is discoverable through standard SDK message inspection.

### Inline Attributes on `EntityMetadata`

When using `CreateEntities`, attributes can be defined inline within `EntityMetadata.Attributes`:

```csharp
var entity = new EntityMetadata
{
    SchemaName = "prefix_TableName",
    DisplayName = new Label("Table Name", 1033),
    DisplayCollectionName = new Label("Table Names", 1033),
    OwnershipType = OwnershipTypes.UserOwned,
    Attributes = new AttributeMetadata[]
    {
        new StringAttributeMetadata
        {
            SchemaName = "prefix_name",
            IsPrimaryName = true,
            MaxLength = 200,
            RequiredLevel = new AttributeRequiredLevelManagedProperty(
                AttributeRequiredLevel.ApplicationRequired),
            DisplayName = new Label("Name", 1033)
        },
        // ... more attributes
    }
};
```

Relationships can also be defined inline via `ManyToOneRelationships`, `OneToManyRelationships`, and `ManyToManyRelationships` arrays on `EntityMetadata`.

---

## 2. Attribute (Column) Operations

### Creating Attributes

- **SDK**: `CreateAttributeRequest`
- **Web API**: `POST /api/data/v9.2/EntityDefinitions({entityId})/Attributes`

Changes go to an "unpublished" state — an explicit `PublishXmlRequest` is required to make them live. The Web API approach accepts `@odata.type` in the request body to specify the concrete attribute type.

### Updating Attributes

#### SDK Approach: `UpdateAttributeRequest`

Retrieves current metadata, modifies properties, sends it back:

```csharp
var request = new UpdateAttributeRequest
{
    EntityLogicalName = "account",
    Attribute = updatedAttribute,
    MergeLabels = true
};
```

**Known limitation**: `UpdateAttributeRequest` does **not** persist `RequiredLevel` (managed property) changes. The server accepts the request without error but silently ignores the `RequiredLevel` value.

Works correctly for: `DisplayName`, `Description`, `MaxLength`, `Format`, `Precision`, `MinValue`, `MaxValue`, and other standard properties.

#### Web API Approach: `PUT` (Full Replacement)

```
PUT /api/data/v9.2/EntityDefinitions({entityId})/Attributes({attributeId})/Microsoft.Dynamics.CRM.{Type}AttributeMetadata
```

Sends the complete attribute definition as JSON.

- **Does persist `RequiredLevel` changes** — this is the approach observed in use by make.powerapps.com
- Use the `MSCRM.MergeLabels: false` header to control label behavior
- The `@odata.type` cast must be in the URL path (not just the body)

#### What Triggers DDL vs. Metadata-Only Updates

**Metadata-only updates** (fast — no SQL `ALTER TABLE`):
- `DisplayName`, `Description` changes
- `RequiredLevel` changes
- `IsAuditEnabled`, `IsValidForAdvancedFind` changes
- Any label or display property changes

**DDL updates** (slower — triggers `ALTER TABLE`):
- `MaxLength` changes on string/memo columns
- Type or precision changes
- Nullable changes
- Default value changes
- Calculated field definition changes

**Optimization**: When updating attributes, include only the properties you are actually changing. If you only change display metadata (labels, `RequiredLevel`), the server skips expensive DDL operations entirely and only updates metadata tables.

### `MergeLabels` Parameter

Available on `UpdateEntityRequest`, `UpdateAttributeRequest`, `UpdateRelationshipRequest`, and `UpdateOptionSetRequest`.

| Context | Usage |
|---|---|
| SDK | `request.MergeLabels = true` |
| Web API | `MSCRM.MergeLabels: true` request header |

- **`true`**: Only adds or updates the provided language labels; preserves all others
- **`false`**: Replaces ALL labels with the provided set (requires a complete label set)

Use `true` for targeted updates to avoid unnecessary label processing.

---

## 3. Publishing Customizations

### What Publishing Does

Publishing promotes metadata changes from an "unpublished" (draft) state to a "published" (live) state. The server processes multiple component types sequentially:

- Forms (`systemform`)
- Views (`savedquery`)
- Option set labels (`stringmap`)
- Ribbons and command bars
- Sitemaps
- Dashboards, web resources, app modules

Each component type is processed in its own transaction with exclusive locking.

### Why Publishing Is Slow

- Acquires an **exclusive organization-wide lock** — only one publish operation can run at a time across the entire org
- Loads entity metadata from the database
- Processes each component type sequentially
- Regenerates client-side ribbon metadata for customized entities
- Invalidates metadata caches across all servers in the deployment

### Optimization Strategies

#### 1. Batch Entities in a Single `PublishXmlRequest`

```xml
<importexportxml>
  <entities>
    <entity>account</entity>
    <entity>contact</entity>
    <entity>prefix_mytable</entity>
  </entities>
</importexportxml>
```

Publishing 10 entities in one call is roughly **10× faster** than 10 separate publish calls because:
- The lock is acquired once (not 10 times)
- Metadata is loaded once
- Cache invalidation happens once

#### 2. Always Use Targeted Publish — Never `PublishAllXmlRequest`

`PublishAllXml` uses a heavier exclusive lock, loads ALL entities, and processes every component type across the entire organization. Always use `PublishXml` with specific entity names.

#### 3. Batch All Changes First, Publish Once at the End

> **CLI usage:** The [changeset apply pipeline](changeset-staging.md#the-4-phase-apply-pipeline) implements this pattern automatically — all schema mutations in a changeset are applied first, then a single targeted `PublishXml` is issued for all affected entities at the end of Phase 1b.

Instead of:
> Create attr → Publish → Create attr → Publish → Create attr → Publish

Do:
> Create attr → Create attr → Create attr → **Publish (once)**

#### 4. Entity Creation Auto-Publishes Initial Components

After `CreateEntityRequest` or `CreateEntities`, the initial forms, views, and sitemap entries are automatically published as part of the creation process. You do **not** need to call `PublishXml` after entity creation — only after subsequent modifications to that entity.

#### 5. Consider `PublishAsync` for Non-Blocking Operations

The `PublishAsync` and `PublishAllAsync` messages run publish as a background system job. Useful in CI/CD pipelines where you do not need to wait for the publish to complete before proceeding.

#### 6. Multi-Component Publish XML

You can batch different component types in a single publish:

```xml
<importexportxml>
  <entities><entity>account</entity></entities>
  <optionsets><optionset>{guid}</optionset></optionsets>
  <sitemaps><sitemap>{guid}</sitemap></sitemaps>
  <webresources><webresource>{guid}</webresource></webresources>
  <dashboards><dashboard>{guid}</dashboard></dashboards>
</importexportxml>
```

---

## 4. Form and View Updates

### Efficient Batch Pattern

Forms (`systemform`) and views (`savedquery`) are standard Dataverse entities. Updates to their XML content (`FormXml`, `FetchXml`, `LayoutXml`) go to an unpublished state:

1. **Update N forms/views** via standard CRUD (`Update` request on `systemform` / `savedquery`)
2. **Publish once** with all affected entities in a single `PublishXml` call

This is dramatically faster than updating and publishing each form individually.

### Form XML Is Full-Replacement

There is no incremental or diff-based form update — you must send the complete `FormXml`. Build the full XML client-side and `PUT` it.

---

## 5. Solution Context

### `MSCRM.SolutionUniqueName` Header

Instead of explicitly calling `AddSolutionComponent` after creating an entity or attribute, use the `MSCRM.SolutionUniqueName` request header (SDK) or body parameter (Web API). The server automatically adds the component to the specified solution during creation.

This pattern is observed in use by Power Apps Data Workspace and Power Apps Vibe Coding.

### `GetPreferredSolution`

Available as an SDK message — returns the user's preferred solution for the current environment. Useful for determining the default solution to target when no explicit solution is specified.

### `msdyn_solutioncomponentsummaries`

A virtual entity that provides efficient querying of solution components:

```
GET /api/data/v9.2/msdyn_solutioncomponentsummaries?$filter=(msdyn_componenttype eq 1)
```

Component type `1` = Entity. This is faster than querying `EntityDefinitions` with solution filtering for inventory purposes.

---

## 6. Metadata Cache Consistency

### `ForceServerMetadataCacheConsistency`

A property on `ServiceClient` — when set to `true`, forces the server to refresh its metadata cache from the database on every request.

```csharp
serviceClient.ForceServerMetadataCacheConsistency = true;
```

Use this immediately after publish operations to ensure reads return the latest published state.

> **Warning**: This significantly slows down operations. Use only for reads that **must** see the latest published state, and disable it afterward.

### Typical Cache Refresh Timing

After `PublishXml`, metadata changes typically propagate within 1–5 seconds without `ForceServerMetadataCacheConsistency`. For immediate consistency guarantees, set the flag on the read connection temporarily.

---

## 7. Summary: Performance Cheat Sheet

| Operation | Slow Way | Fast Way |
|---|---|---|
| Create 5 entities | 5× `CreateEntityRequest` (5–10 min) | 1× `CreateEntities` (15–20s) |
| Add 10 attributes | 10× `CreateAttribute` + 10× `Publish` | 10× `CreateAttribute` + 1× `Publish` |
| Update display name | `UpdateAttribute` + `Publish` | Web API `PUT` + `Publish` (same speed, but `PUT` handles `RequiredLevel`) |
| Update `RequiredLevel` | SDK `UpdateAttributeRequest` ❌ silently ignored | Web API `PUT` full replacement ✅ |
| Update 5 forms | 5× (update + publish) | 5× update + 1× `PublishXml` |
| Check metadata after publish | `RetrieveAsIfPublished=true` (may be cached) | Set `ForceServerMetadataCacheConsistency=true` on `ServiceClient` |

---

## 8. API Reference

| API | Method | Usage |
|---|---|---|
| `CreateEntities` | SDK `OrganizationRequest` | Batch entity creation with inline attributes and relationships |
| `PublishXmlRequest` | SDK | Targeted publish with entity XML |
| `PublishAllXmlRequest` | SDK | Full org publish (avoid in automation) |
| `PublishAsync` / `PublishAllAsync` | SDK | Background publish as system job |
| `GetPreferredSolution` | SDK | Get user's default solution |
| `PUT .../Attributes({id})/Microsoft.Dynamics.CRM.{Type}` | Web API | Full attribute replacement (handles `RequiredLevel`) |
| `POST .../EntityDefinitions({id})/Attributes` | Web API | Add new attribute to existing entity |
| `msdyn_solutioncomponentsummaries` | OData query | Efficient solution component listing |
| `ForceServerMetadataCacheConsistency` | `ServiceClient` property | Force fresh metadata reads |

---

## 9. Known Limitations

- `data record create --data` JSON input passes values as-is to Dataverse. Choice (`OptionSetValue`) and `Money` fields may require server-side type conversion that isn't always automatic. For bulk data loading, prefer CMT import packages.

---

## References

- [Create and update column definitions using the Web API](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/create-update-column-definitions-using-web-api)
- [Publish customizations](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/org-service/metadata-retrieve-update-delete-entities)
- [Multi-table lookup columns](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/multitable-lookup)
- [ForceServerMetadataCacheConsistency](https://learn.microsoft.com/en-us/dotnet/api/microsoft.powerplatform.dataverse.client.serviceclient.forceservermetadatacacheconsistency)
- [Column requirement level](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/entity-attribute-metadata#column-requirement-level)
- [Create and update entity definitions using the Web API](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/create-update-entity-definitions-using-web-api)
