namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// A Dataverse connection reference — the solution component a solution-aware
/// cloud flow binds its connector actions to.
/// </summary>
/// <param name="LogicalName">The value a flow definition puts in <c>connection.connectionReferenceLogicalName</c>.</param>
/// <param name="ConnectorId">The connector's API id, e.g. <c>/providers/Microsoft.PowerApps/apis/shared_teams</c>.</param>
/// <param name="ConnectionId">The connection currently bound, or <see langword="null"/> when the reference is unbound.</param>
public sealed record ConnectionReferenceRecord(
    Guid Id,
    string LogicalName,
    string? DisplayName,
    string? ConnectorId,
    string? ConnectionId);

public interface IConnectionReferenceService
{
    /// <summary>
    /// Lists active connection references in the environment, optionally only those
    /// for one connector (matched by connector name, e.g. <c>shared_teams</c>).
    /// </summary>
    Task<IReadOnlyList<ConnectionReferenceRecord>> ListAsync(
        string? profileName, string? connector, CancellationToken ct);
}
