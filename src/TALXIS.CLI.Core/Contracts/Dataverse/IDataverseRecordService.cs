using System.Text.Json;

namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Single-record CRUD operations against a live Dataverse environment.
/// All calls go through <c>ServiceClient</c>.
/// </summary>
public interface IDataverseRecordService
{
    /// <summary>
    /// Retrieves a single record by entity logical name and ID.
    /// Returns the record as a JSON object.
    /// </summary>
    Task<JsonElement> GetAsync(
        string? profileName,
        string entityLogicalName,
        Guid recordId,
        string[]? columns,
        bool includeAnnotations,
        CancellationToken ct);

    /// <summary>
    /// Creates a new record from a JSON object.
    /// Returns the ID of the created record.
    /// </summary>
    Task<Guid> CreateAsync(
        string? profileName,
        string entityLogicalName,
        JsonElement attributes,
        CancellationToken ct);

    /// <summary>
    /// Updates an existing record with the given attribute values.
    /// </summary>
    Task UpdateAsync(
        string? profileName,
        string entityLogicalName,
        Guid recordId,
        JsonElement attributes,
        CancellationToken ct);

    /// <summary>
    /// Deletes a record by entity logical name and ID.
    /// </summary>
    Task DeleteAsync(
        string? profileName,
        string entityLogicalName,
        Guid recordId,
        CancellationToken ct);
}
