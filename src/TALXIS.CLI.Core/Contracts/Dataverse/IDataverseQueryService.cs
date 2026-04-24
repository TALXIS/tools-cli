using System.Text.Json;

namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Result of a Dataverse query — a list of records as JSON elements plus
/// optional total count. Returned by all query methods on
/// <see cref="IDataverseQueryService"/>.
/// </summary>
public sealed record DataverseQueryResult(
    IReadOnlyList<JsonElement> Records,
    int? TotalCount);

/// <summary>
/// Executes read-only queries against a live Dataverse environment.
/// Supports SQL (Web API <c>?sql=</c>), FetchXML, and OData query modes.
/// All calls go through <c>ServiceClient</c>.
/// </summary>
public interface IDataverseQueryService
{
    /// <summary>
    /// Runs a T-SQL subset query via the Dataverse Web API <c>?sql=</c>
    /// parameter. Automatically paginates and strips OData annotations
    /// unless <paramref name="includeAnnotations"/> is <c>true</c>.
    /// </summary>
    Task<DataverseQueryResult> QuerySqlAsync(
        string? profileName,
        string sql,
        int? top,
        bool includeAnnotations,
        CancellationToken ct);

    /// <summary>
    /// Runs a FetchXML query via <c>RetrieveMultiple</c>. Automatically
    /// paginates using paging cookies.
    /// </summary>
    Task<DataverseQueryResult> QueryFetchXmlAsync(
        string? profileName,
        string fetchXml,
        int? top,
        bool includeAnnotations,
        CancellationToken ct);

    /// <summary>
    /// Runs an OData query via <c>ServiceClient.ExecuteWebRequest</c>.
    /// Automatically follows <c>@odata.nextLink</c> for pagination.
    /// </summary>
    Task<DataverseQueryResult> QueryODataAsync(
        string? profileName,
        string entitySetOrPath,
        string? select,
        string? filter,
        string? orderBy,
        int? top,
        bool includeAnnotations,
        CancellationToken ct);
}
