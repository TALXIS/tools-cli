using Microsoft.Xrm.Sdk;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Data;

/// <summary>
/// Implements many-to-many (N:N) relationship associate/disassociate
/// operations against a live Dataverse environment.
/// </summary>
internal sealed class DataverseRelationshipService : IDataverseRelationshipService
{
    public async Task AssociateAsync(
        string? profileName,
        string entityName,
        Guid recordId,
        string targetEntityName,
        Guid targetRecordId,
        string relationshipName,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        conn.Client.Associate(
            entityName,
            recordId,
            new Relationship(relationshipName),
            new EntityReferenceCollection { new EntityReference(targetEntityName, targetRecordId) });
    }

    public async Task DisassociateAsync(
        string? profileName,
        string entityName,
        Guid recordId,
        string targetEntityName,
        Guid targetRecordId,
        string relationshipName,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        conn.Client.Disassociate(
            entityName,
            recordId,
            new Relationship(relationshipName),
            new EntityReferenceCollection { new EntityReference(targetEntityName, targetRecordId) });
    }
}
