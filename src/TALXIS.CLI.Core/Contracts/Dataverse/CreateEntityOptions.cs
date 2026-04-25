namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Options for creating a new entity (table) in Dataverse.
/// Used by <see cref="IDataverseEntityMetadataService.CreateEntityAsync"/>.
/// </summary>
public sealed record CreateEntityOptions
{
    /// <summary>The schema name of the new entity (e.g. "new_myentity").</summary>
    public required string SchemaName { get; init; }

    /// <summary>The display name (label) for the entity.</summary>
    public required string DisplayName { get; init; }

    /// <summary>The plural display name (label) for the entity.</summary>
    public required string PluralName { get; init; }

    /// <summary>An optional description for the entity.</summary>
    public string? Description { get; init; }

    /// <summary>The unique name of the solution to add the entity to.</summary>
    public string? Solution { get; init; }

    /// <summary>
    /// Table ownership: "user" (default) — records owned by users/teams;
    /// "organization" — records owned by the org (no user-level access control).
    /// </summary>
    public string Ownership { get; init; } = "user";

    /// <summary>
    /// Table type: "standard" (default) — SQL-backed table;
    /// "activity" — activity table with subject, dates, parties;
    /// "elastic" — Azure Cosmos DB-backed for very large datasets.
    /// </summary>
    public string TableType { get; init; } = "standard";

    /// <summary>Enable notes and file attachments on this table.</summary>
    public bool HasNotes { get; init; }

    /// <summary>Enable associating activities (emails, tasks, appointments) with records.</summary>
    public bool HasActivities { get; init; }

    /// <summary>Enable auditing for this table to track data changes.</summary>
    public bool EnableAudit { get; init; }

    /// <summary>Enable change tracking for data synchronization scenarios.</summary>
    public bool EnableChangeTracking { get; init; }
}
