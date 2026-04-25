namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Many-to-many (N:N) relationship associate/disassociate operations
/// against a live Dataverse environment.
/// </summary>
public interface IDataverseRelationshipService
{
    /// <summary>
    /// Links two records together through a many-to-many relationship.
    /// </summary>
    Task AssociateAsync(
        string? profileName,
        string entityName,
        Guid recordId,
        string targetEntityName,
        Guid targetRecordId,
        string relationshipName,
        CancellationToken ct);

    /// <summary>
    /// Removes the link between two records in a many-to-many relationship.
    /// </summary>
    Task DisassociateAsync(
        string? profileName,
        string entityName,
        Guid recordId,
        string targetEntityName,
        Guid targetRecordId,
        string relationshipName,
        CancellationToken ct);
}
