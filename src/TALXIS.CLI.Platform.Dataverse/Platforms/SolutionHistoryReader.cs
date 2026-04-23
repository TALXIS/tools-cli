using TALXIS.CLI.Core.Platforms.Dataverse;
using TALXIS.CLI.Platform.Dataverse;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace TALXIS.CLI.Platform.Dataverse.Platforms;

/// <summary>
/// Reader for <c>msdyn_solutionhistory</c>. The virtual entity rejects arbitrary
/// server-side filter conditions; name and activity-id lookups are matched client-side.
/// </summary>
public sealed class SolutionHistoryReader
{
    private const string EntityName = DeploymentSchema.SolutionHistory.EntityName;
    private static readonly ColumnSet Columns = new(
        "msdyn_solutionhistoryid",
        "msdyn_name",
        "msdyn_uniquename",
        "msdyn_solutionid",
        "msdyn_solutionversion",
        "msdyn_packagename",
        "msdyn_packageversion",
        "msdyn_operation",
        "msdyn_suboperation",
        "msdyn_isoverwritecustomizations",
        "msdyn_starttime",
        "msdyn_endtime",
        "msdyn_status",
        "msdyn_solutionhistorydescription",
        "msdyn_activityid");

    private readonly IOrganizationServiceAsync2 _service;
    private readonly ILogger? _logger;

    public SolutionHistoryReader(IOrganizationServiceAsync2 service, ILogger? logger = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger;
    }

    /// <summary>
    /// Returns recent <c>msdyn_solutionhistory</c> rows ordered by <c>msdyn_starttime</c> desc.
    /// Optional <paramref name="sinceUtc"/> lower bound and <paramref name="problemsOnly"/>
    /// filter (keeps rows whose status label is not <c>Success</c>/<c>Completed</c>).
    /// </summary>
    public async Task<IReadOnlyList<SolutionHistoryRecord>> GetRecentAsync(
        int count,
        DateTime? sinceUtc = null,
        bool problemsOnly = false,
        CancellationToken ct = default)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count), "count must be > 0.");
        var q = new QueryExpression(EntityName)
        {
            ColumnSet = Columns,
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        q.AddOrder("msdyn_starttime", OrderType.Descending);
        // msdyn_starttime is a virtual/denormalised column on this table and does not reliably
        // support server-side datetime conditions; filtering is applied client-side instead.
        q.TopCount = sinceUtc is not null ? 2000 : (problemsOnly ? Math.Max(count * 4, 50) : count);
        var res = await _service.RetrieveMultipleAsync(q, ct).ConfigureAwait(false);
        IEnumerable<SolutionHistoryRecord> records = res.Entities.Select(ToRecord);
        if (sinceUtc is { } since)
        {
            var floor = DataverseDateTime.EnsureUtc(since);
            records = records.Where(r => r.StartedAtUtc is null || r.StartedAtUtc >= floor);
        }
        if (problemsOnly)
        {
            records = records.Where(r =>
            {
                var s = r.Result;
                if (string.IsNullOrWhiteSpace(s)) return true;
                return !s.Contains("success", StringComparison.OrdinalIgnoreCase)
                    && !s.Contains("completed", StringComparison.OrdinalIgnoreCase);
            });
        }
        return records.Take(count).ToList();
    }

    /// <summary>
    /// Retrieves a single <c>msdyn_solutionhistory</c> row by id, or <c>null</c> if not found.
    /// </summary>
    public async Task<SolutionHistoryRecord?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var q = new QueryExpression(EntityName)
            {
                ColumnSet = Columns,
                TopCount = 1,
                Criteria = new FilterExpression(LogicalOperator.And),
            };
            q.Criteria.AddCondition("msdyn_solutionhistoryid", ConditionOperator.Equal, id);
            var res = await _service.RetrieveMultipleAsync(q, ct).ConfigureAwait(false);
            if (res.Entities.Count > 0)
            {
                return ToRecord(res.Entities[0]);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Solution history query-by-id failed. Falling back to direct retrieve.");
        }

        try
        {
            var entity = await _service.RetrieveAsync(EntityName, id, Columns, ct).ConfigureAwait(false);
            return entity is null ? null : ToRecord(entity);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the latest <c>msdyn_solutionhistory</c> row whose unique name matches
    /// <paramref name="solutionName"/> (case-insensitive). Returns <c>null</c> when no match.
    /// </summary>
    public async Task<SolutionHistoryRecord?> GetLatestByNameAsync(string solutionName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionName);
        // msdyn_solutionhistory is a virtual entity; its plugin rejects arbitrary server-side
        // conditions. Fetch a large window and match client-side.
        var q = new QueryExpression(EntityName) { ColumnSet = Columns, TopCount = 500 };
        q.AddOrder("msdyn_starttime", OrderType.Descending);
        var res = await _service.RetrieveMultipleAsync(q, ct).ConfigureAwait(false);
        return res.Entities.Select(ToRecord)
            .FirstOrDefault(r => (r.SolutionName ?? "").Equals(solutionName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns the <c>msdyn_solutionhistory</c> row whose <c>msdyn_activityid</c> matches
    /// <paramref name="asyncOperationId"/> — the <c>asyncoperation.asyncoperationid</c> returned
    /// by <c>ImportSolutionAsyncRequest</c> / <c>StageAndUpgradeAsyncRequest</c>.
    /// <para>
    /// Uses a time-bounded fetch when <paramref name="nearUtc"/> is provided, otherwise falls
    /// back to fetching the 2 000 most-recent rows ordered by <c>msdyn_starttime</c> descending.
    /// The virtual entity rejects arbitrary server-side conditions, so matching is client-side.
    /// </para>
    /// </summary>
    public async Task<SolutionHistoryRecord?> GetByActivityIdAsync(
        Guid asyncOperationId,
        DateTime? nearUtc = null,
        CancellationToken ct = default)
    {
        IEnumerable<SolutionHistoryRecord> records;

        if (nearUtc is { } pivot)
        {
            // Narrow to a ±10-minute window around the known operation time so we don't
            // scan the entire history on busy environments.
            var windowStart = pivot - TimeSpan.FromMinutes(10);
            var windowEnd = pivot + TimeSpan.FromMinutes(10);
            records = await GetInTimeWindowAsync(windowStart, windowEnd, ct).ConfigureAwait(false);
        }
        else
        {
            var q = new QueryExpression(EntityName) { ColumnSet = Columns, TopCount = 2000 };
            q.AddOrder("msdyn_starttime", OrderType.Descending);
            var res = await _service.RetrieveMultipleAsync(q, ct).ConfigureAwait(false);
            records = res.Entities.Select(ToRecord);
        }

        return records.FirstOrDefault(r => r.ActivityId == asyncOperationId);
    }

    /// <summary>
    /// Returns solution history rows correlated to a package run via the <c>asyncoperation</c> entity.
    /// <para>
    /// Package Deployer sets <c>x-ms-client-session-id</c> = <paramref name="correlationId"/> on every SDK call.
    /// Dataverse records this as <c>asyncoperation.correlationid</c> for each import async job.
    /// <c>msdyn_solutionhistory.msdyn_activityid</c> holds the <c>asyncoperation.asyncoperationid</c>
    /// of the platform's internal import job, providing an exact join.
    /// </para>
    /// <para>
    /// Returns empty when <c>asyncoperation</c> records have been cleaned up by Dataverse's
    /// async job retention policy (typically ~30 days). Callers should fall back to
    /// <see cref="GetInTimeWindowAsync"/> in that case.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<SolutionHistoryRecord>> GetByCorrelationIdAsync(
        Guid correlationId,
        CancellationToken ct = default)
    {
        // Step 1: resolve asyncoperation IDs for this package run.
        // asyncoperation is a standard table so server-side conditions work fine.
        var asyncOpQuery = new QueryExpression(DataverseSchema.AsyncOperation.EntityName)
        {
            ColumnSet = new ColumnSet("asyncoperationid"),
            Criteria = new FilterExpression(LogicalOperator.And),
            TopCount = 200,
        };
        asyncOpQuery.Criteria.AddCondition("correlationid", ConditionOperator.Equal, correlationId);
        var asyncOps = await _service.RetrieveMultipleAsync(asyncOpQuery, ct).ConfigureAwait(false);
        if (asyncOps.Entities.Count == 0)
        {
            _logger?.LogDebug("No asyncoperation rows found for correlationid {CorrelationId}; async jobs may have been cleaned up.", correlationId);
            return Array.Empty<SolutionHistoryRecord>();
        }

        var asyncOpIds = asyncOps.Entities.Select(e => e.Id).ToHashSet();
        _logger?.LogDebug("Found {Count} asyncoperation rows for correlationid {CorrelationId}.", asyncOpIds.Count, correlationId);

        // Step 2: fetch solution history and filter client-side by msdyn_activityid.
        var q = new QueryExpression(EntityName) { ColumnSet = Columns, TopCount = 500 };
        q.AddOrder("msdyn_starttime", OrderType.Descending);
        var res = await _service.RetrieveMultipleAsync(q, ct).ConfigureAwait(false);
        return res.Entities.Select(ToRecord)
            .Where(r => r.ActivityId is { } id && asyncOpIds.Contains(id))
            .OrderBy(r => r.StartedAtUtc)
            .ToList();
    }

    public async Task<IReadOnlyList<SolutionHistoryRecord>> GetInTimeWindowAsync(
        DateTime windowStartUtc,
        DateTime windowEndUtc,
        CancellationToken ct = default)
    {
        var start = DataverseDateTime.EnsureUtc(windowStartUtc);
        var end = DataverseDateTime.EnsureUtc(windowEndUtc);

        // msdyn_solutionhistory is a virtual entity; its plugin rejects arbitrary server-side
        // conditions — fetch a large window ordered by starttime and filter client-side.
        var q = new QueryExpression(EntityName) { ColumnSet = Columns, TopCount = 500 };
        q.AddOrder("msdyn_starttime", OrderType.Descending);

        var res = await _service.RetrieveMultipleAsync(q, ct).ConfigureAwait(false);
        return res.Entities.Select(ToRecord)
            .Where(r => r.StartedAtUtc >= start && r.StartedAtUtc <= end)
            .OrderBy(r => r.StartedAtUtc)
            .ToList();
    }

    internal static SolutionHistoryRecord ToRecord(Entity e)
    {
        int? operation = e.GetAttributeValue<OptionSetValue>("msdyn_operation")?.Value;
        int? suboperation = e.GetAttributeValue<OptionSetValue>("msdyn_suboperation")?.Value;

        // Prefer Dataverse's own formatted values; fall back to our hardcoded map.
        string operationLabel = e.FormattedValues.TryGetValue("msdyn_operation", out var opFmt) && !string.IsNullOrWhiteSpace(opFmt)
            ? opFmt
            : SolutionHistoryMappings.MapOperation(operation);
        string suboperationLabel = e.FormattedValues.TryGetValue("msdyn_suboperation", out var subFmt) && !string.IsNullOrWhiteSpace(subFmt)
            ? subFmt
            : SolutionHistoryMappings.MapSuboperation(suboperation);

        DateTime? start = e.Contains("msdyn_starttime")
            ? DataverseDateTime.EnsureUtc(e.GetAttributeValue<DateTime>("msdyn_starttime"))
            : null;
        DateTime? end = e.Contains("msdyn_endtime")
            ? DataverseDateTime.EnsureUtc(e.GetAttributeValue<DateTime>("msdyn_endtime"))
            : null;

        bool? overwrite = e.Contains("msdyn_isoverwritecustomizations")
            ? e.GetAttributeValue<bool>("msdyn_isoverwritecustomizations")
            : null;

        string? status = null;
        if (e.FormattedValues.TryGetValue("msdyn_status", out var statusFormatted) && !string.IsNullOrWhiteSpace(statusFormatted))
        {
            status = statusFormatted;
        }
        else if (e.Contains("msdyn_status"))
        {
            status = e.GetAttributeValue<OptionSetValue>("msdyn_status")?.Value.ToString();
        }

        var description = e.GetAttributeValue<string>("msdyn_solutionhistorydescription");

        Guid? activityId = null;
        if (e.Contains("msdyn_activityid"))
        {
            var raw = e.GetAttributeValue<string>("msdyn_activityid");
            if (Guid.TryParse(raw, out var parsed))
                activityId = parsed;
        }

        return new SolutionHistoryRecord(
            Id: e.Id,
            SolutionName: e.GetAttributeValue<string>("msdyn_uniquename") ?? e.GetAttributeValue<string>("msdyn_name"),
            SolutionVersion: e.GetAttributeValue<string>("msdyn_solutionversion"),
            PackageName: e.GetAttributeValue<string>("msdyn_packagename"),
            OperationCode: operation,
            OperationLabel: operationLabel,
            SuboperationCode: suboperation,
            SuboperationLabel: suboperationLabel,
            OverwriteUnmanagedCustomizations: overwrite,
            StartedAtUtc: start,
            CompletedAtUtc: end,
            Result: status ?? description,
            ActivityId: activityId);
    }
}
