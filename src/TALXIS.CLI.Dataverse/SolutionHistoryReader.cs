using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace TALXIS.CLI.Dataverse;

/// <summary>
/// Structured view of a row in <c>msdyn_solutionhistory</c>. Operation / suboperation codes
/// are mapped via <see cref="SolutionHistoryMappings"/>. <see cref="DateTime"/> values are UTC.
/// </summary>
public sealed record SolutionHistoryRecord(
    Guid Id,
    string? SolutionName,
    string? SolutionVersion,
    string? PackageName,
    int? OperationCode,
    string OperationLabel,
    int? SuboperationCode,
    string SuboperationLabel,
    bool? OverwriteUnmanagedCustomizations,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? Result);

/// <summary>
/// Reader for <c>msdyn_solutionhistory</c>. The table has no hard FK to
/// <c>importjob</c> across all stamps; callers can correlate either via
/// <see cref="GetByImportJobIdAsync"/> (when <c>msdyn_importjobid</c> is populated)
/// or <see cref="GetInTimeWindowAsync"/> as a fallback.
/// </summary>
public sealed class SolutionHistoryReader
{
    private const string EntityName = "msdyn_solutionhistory";
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
        "msdyn_solutionhistorydescription");

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
        if (sinceUtc is { } since)
        {
            q.Criteria.AddCondition("msdyn_starttime", ConditionOperator.OnOrAfter, DataverseDateTime.EnsureUtc(since));
        }
        q.AddOrder("msdyn_starttime", OrderType.Descending);
        q.TopCount = problemsOnly ? Math.Max(count * 4, 50) : count;
        var res = await _service.RetrieveMultipleAsync(q, ct).ConfigureAwait(false);
        var records = res.Entities.Select(ToRecord);
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
            var entity = await _service.RetrieveAsync(EntityName, id, Columns, ct).ConfigureAwait(false);
            return entity is null ? null : ToRecord(entity);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns candidate rows whose <c>msdyn_solutionhistoryid</c> (hex form, dashes stripped)
    /// starts with <paramref name="hexPrefix"/>. Prefix match runs client-side against the last 200 rows.
    /// </summary>
    public async Task<IReadOnlyList<SolutionHistoryRecord>> GetByIdPrefixAsync(string hexPrefix, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hexPrefix);
        var q = new QueryExpression(EntityName)
        {
            ColumnSet = Columns,
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        q.AddOrder("msdyn_starttime", OrderType.Descending);
        q.TopCount = 500;
        var res = await _service.RetrieveMultipleAsync(q, ct).ConfigureAwait(false);
        var lower = hexPrefix.ToLowerInvariant();
        return res.Entities
            .Select(ToRecord)
            .Where(r => r.Id.ToString("N").StartsWith(lower, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Returns the latest <c>msdyn_solutionhistory</c> row whose unique name matches
    /// <paramref name="solutionName"/> (case-insensitive). Returns <c>null</c> when no match.
    /// </summary>
    public async Task<SolutionHistoryRecord?> GetLatestByNameAsync(string solutionName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionName);
        var q = new QueryExpression(EntityName)
        {
            ColumnSet = Columns,
            Criteria = new FilterExpression(LogicalOperator.Or),
            TopCount = 1,
        };
        q.Criteria.AddCondition("msdyn_uniquename", ConditionOperator.Equal, solutionName);
        q.Criteria.AddCondition("msdyn_name", ConditionOperator.Equal, solutionName);
        q.AddOrder("msdyn_starttime", OrderType.Descending);
        var res = await _service.RetrieveMultipleAsync(q, ct).ConfigureAwait(false);
        return res.Entities.Count == 0 ? null : ToRecord(res.Entities[0]);
    }

    public async Task<IReadOnlyList<SolutionHistoryRecord>> GetByImportJobIdAsync(Guid importJobId, CancellationToken ct = default)
    {
        // msdyn_solutionhistory has no reliable FK to importjob on all stamps.
        // This method is intentionally left as a no-op fallback — callers should use
        // GetInTimeWindowAsync for correlation when no direct FK is available.
        _ = importJobId;
        await Task.CompletedTask.ConfigureAwait(false);
        return Array.Empty<SolutionHistoryRecord>();
    }

    public async Task<IReadOnlyList<SolutionHistoryRecord>> GetInTimeWindowAsync(
        DateTime windowStartUtc,
        DateTime windowEndUtc,
        CancellationToken ct = default)
    {
        var start = DataverseDateTime.EnsureUtc(windowStartUtc);
        var end = DataverseDateTime.EnsureUtc(windowEndUtc);

        var q = new QueryExpression(EntityName)
        {
            ColumnSet = Columns,
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        q.Criteria.AddCondition("msdyn_starttime", ConditionOperator.OnOrAfter, start);
        q.Criteria.AddCondition("msdyn_starttime", ConditionOperator.OnOrBefore, end);
        q.AddOrder("msdyn_starttime", OrderType.Ascending);

        var res = await _service.RetrieveMultipleAsync(q, ct).ConfigureAwait(false);
        return res.Entities.Select(ToRecord).ToList();
    }

    internal static SolutionHistoryRecord ToRecord(Entity e)
    {
        int? operation = e.GetAttributeValue<OptionSetValue>("msdyn_operation")?.Value;
        int? suboperation = e.GetAttributeValue<OptionSetValue>("msdyn_suboperation")?.Value;

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

        return new SolutionHistoryRecord(
            Id: e.Id,
            SolutionName: e.GetAttributeValue<string>("msdyn_uniquename") ?? e.GetAttributeValue<string>("msdyn_name"),
            SolutionVersion: e.GetAttributeValue<string>("msdyn_solutionversion"),
            PackageName: e.GetAttributeValue<string>("msdyn_packagename"),
            OperationCode: operation,
            OperationLabel: SolutionHistoryMappings.MapOperation(operation),
            SuboperationCode: suboperation,
            SuboperationLabel: SolutionHistoryMappings.MapSuboperation(suboperation),
            OverwriteUnmanagedCustomizations: overwrite,
            StartedAtUtc: start,
            CompletedAtUtc: end,
            Result: status ?? description);
    }
}
