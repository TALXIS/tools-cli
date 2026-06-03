namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Parsed, source-agnostic filter applied to environment-log reads. Built once
/// by a leaf command from its CLI flags and passed straight to the reader so the
/// service signatures stay small.
/// </summary>
/// <param name="SinceUtc">Lower bound on the row timestamp (<c>createdon</c>). <c>null</c> = no bound.</param>
/// <param name="Entity">Logical name of an entity to filter by (plug-in <c>primaryentity</c> / async regarding object). <c>null</c> = all.</param>
/// <param name="Plugin">Plug-in / activity type-name substring to filter by (plug-in traces only). <c>null</c> = all.</param>
/// <param name="ErrorsOnly">When <c>true</c>, keep only error rows (traces with an exception, failed/cancelled jobs).</param>
/// <param name="CorrelationId">Restrict to a single operation's correlation id. <c>null</c> = all.</param>
/// <param name="Top">Maximum number of rows to return.</param>
public sealed record EnvironmentLogFilter(
    DateTime? SinceUtc,
    string? Entity,
    string? Plugin,
    bool ErrorsOnly,
    Guid? CorrelationId,
    int Top);

/// <summary>
/// Row from the Dataverse <c>plugintracelog</c> table. All <see cref="DateTime"/>
/// values are UTC.
/// </summary>
public sealed record PluginTraceRecord(
    Guid Id,
    DateTime? CreatedOnUtc,
    string? TypeName,
    string? MessageName,
    string? PrimaryEntity,
    string? Mode,
    int? Depth,
    long? DurationMs,
    bool HasException,
    string? ExceptionSnippet,
    string? MessageSnippet,
    Guid? CorrelationId);

/// <summary>
/// Row from the Dataverse <c>asyncoperation</c> table (background jobs, including
/// classic workflow runs). All <see cref="DateTime"/> values are UTC.
/// </summary>
public sealed record AsyncJobRecord(
    Guid Id,
    string? Name,
    int? OperationTypeCode,
    string? OperationTypeLabel,
    string? StatusLabel,
    bool IsError,
    DateTime? CreatedOnUtc,
    DateTime? StartedOnUtc,
    DateTime? CompletedOnUtc,
    string? RegardingEntity,
    string? Message,
    Guid? CorrelationId);

/// <summary>
/// Reads runtime diagnostic logs from a live environment — plug-in traces and
/// async jobs (background workflows included). Hides the underlying readers and
/// connection lifetime from feature commands.
/// </summary>
public interface IEnvironmentLogService
{
    /// <summary>
    /// Reads recent <c>plugintracelog</c> rows ordered by <c>createdon</c> descending,
    /// honouring <paramref name="filter"/>.
    /// </summary>
    Task<IReadOnlyList<PluginTraceRecord>> GetPluginTracesAsync(
        string? profileName,
        EnvironmentLogFilter filter,
        CancellationToken ct);

    /// <summary>
    /// Reads recent <c>asyncoperation</c> rows of any operation type, ordered by
    /// <c>createdon</c> descending, honouring <paramref name="filter"/>.
    /// </summary>
    Task<IReadOnlyList<AsyncJobRecord>> GetAsyncJobsAsync(
        string? profileName,
        EnvironmentLogFilter filter,
        CancellationToken ct);

    /// <summary>
    /// Reads recent classic (background) workflow runs — <c>asyncoperation</c> rows
    /// whose operation type is Workflow — ordered by <c>createdon</c> descending,
    /// honouring <paramref name="filter"/>.
    /// </summary>
    Task<IReadOnlyList<AsyncJobRecord>> GetWorkflowRunsAsync(
        string? profileName,
        EnvironmentLogFilter filter,
        CancellationToken ct);
}
