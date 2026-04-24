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

    /// <summary>Case-insensitive contains check that handles null values safely.</summary>
    private static bool Contains(string? value, string search) =>
        value is not null && value.Contains(search, StringComparison.OrdinalIgnoreCase);
}
