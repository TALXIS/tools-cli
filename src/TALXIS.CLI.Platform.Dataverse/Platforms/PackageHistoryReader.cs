using TALXIS.CLI.Config.Platforms.Dataverse;
using TALXIS.CLI.Platform.Dataverse;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace TALXIS.CLI.Platform.Dataverse.Platforms;

/// <summary>
/// Reader for the <c>packagehistory</c> table (Package Deployer run records).
/// Note: <c>packagehistory</c> does not use the <c>msdyn_</c> attribute prefix.
/// Primary name attribute is <c>executionname</c>.
/// </summary>
public sealed class PackageHistoryReader
{
    private const string EntityName = DeploymentSchema.PackageHistory.EntityName;
    private static readonly ColumnSet Columns = new(
        "packagehistoryid",
        "uniquename",
        "executionname",
        "statuscode",
        "stagevalue",
        "operationid",
        "correlationid",
        "statusmessage",
        "createdon",
        "modifiedon");

    private readonly IOrganizationServiceAsync2 _service;
    private readonly ILogger? _logger;

    public PackageHistoryReader(IOrganizationServiceAsync2 service, ILogger? logger = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger;
    }

    public async Task<PackageHistoryRecord?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var q = BuildBaseQuery();
        q.Criteria.AddCondition("packagehistoryid", ConditionOperator.Equal, id);
        q.TopCount = 1;

        var res = await _service.RetrieveMultipleAsync(q, ct).ConfigureAwait(false);
        return res.Entities.Count == 0 ? null : ToRecord(res.Entities[0]);
    }

    /// <summary>
    /// Returns the most recent <paramref name="count"/> package-history rows ordered by
    /// <c>createdon</c> descending.
    /// </summary>
    public async Task<IReadOnlyList<PackageHistoryRecord>> GetRecentAsync(int count = 10, CancellationToken ct = default)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count), "count must be > 0.");
        return await GetRecentAsync(count, sinceUtc: null, problemsOnly: false, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns recent package-history rows, optionally constrained by a <paramref name="sinceUtc"/>
    /// lower bound on <c>createdon</c> and/or filtered to rows whose <c>statuscode</c> is neither
    /// the Success (30) nor the terminal-In-Progress (5) Dataverse system codes.
    /// </summary>
    /// <remarks>
    /// <paramref name="problemsOnly"/> is implemented client-side against the formatted status label
    /// because <c>statuscode</c> option-set values on <c>packagehistory</c> vary across stamps.
    /// The filter accepts rows whose label is not one of: <c>Success</c>, <c>Completed</c>.
    /// Stuck "In Process" rows older than 1h are kept because they are the target of the
    /// <c>StaleInProcess</c> finding.
    /// </remarks>
    public async Task<IReadOnlyList<PackageHistoryRecord>> GetRecentAsync(
        int count,
        DateTime? sinceUtc,
        bool problemsOnly,
        CancellationToken ct = default)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count), "count must be > 0.");
        var q = BuildBaseQuery();
        if (sinceUtc is { } since)
        {
            q.Criteria.AddCondition("createdon", ConditionOperator.GreaterEqual, DataverseDateTime.EnsureUtc(since));
        }
        q.AddOrder("createdon", OrderType.Descending);
        q.TopCount = problemsOnly ? Math.Max(count * 4, 50) : count;
        var res = await _service.RetrieveMultipleAsync(q, ct).ConfigureAwait(false);
        var records = res.Entities.Select(ToRecord);
        if (problemsOnly)
        {
            records = records.Where(r => !IsHealthyStatus(r.Status));
        }
        return records.Take(count).ToList();
    }

    private static bool IsHealthyStatus(string? status) =>
        !string.IsNullOrWhiteSpace(status) &&
        (status.Equals("Success", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Completed", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Latest package-history row whose <c>uniquename</c> matches <paramref name="name"/>
    /// (case-insensitive). Pass <c>null</c> to return the latest overall.
    /// </summary>
    public async Task<PackageHistoryRecord?> GetLatestAsync(string? name = null, CancellationToken ct = default)
    {
        var q = BuildBaseQuery();
        if (!string.IsNullOrWhiteSpace(name))
        {
            q.Criteria.AddCondition("uniquename", ConditionOperator.Equal, name);
        }
        q.AddOrder("createdon", OrderType.Descending);
        q.TopCount = 1;
        var res = await _service.RetrieveMultipleAsync(q, ct).ConfigureAwait(false);
        return res.Entities.Count == 0 ? null : ToRecord(res.Entities[0]);
    }

    private static QueryExpression BuildBaseQuery() => new(EntityName)
    {
        ColumnSet = Columns,
        Criteria = new FilterExpression(LogicalOperator.And),
    };

    private static PackageHistoryRecord ToRecord(Entity e)
    {
        string? StatusLabel()
        {
            if (e.FormattedValues.TryGetValue("statuscode", out var formatted) && !string.IsNullOrWhiteSpace(formatted))
            {
                return formatted;
            }
            return e.GetAttributeValue<OptionSetValue>("statuscode")?.Value.ToString();
        }

        string? StageLabel()
        {
            if (e.FormattedValues.TryGetValue("stagevalue", out var formatted) && !string.IsNullOrWhiteSpace(formatted))
            {
                return formatted;
            }
            var raw = e.GetAttributeValue<object>("stagevalue");
            return raw switch
            {
                OptionSetValue osv => osv.Value.ToString(),
                string s => s,
                _ => raw?.ToString(),
            };
        }

        DateTime? start = e.Contains("createdon")
            ? DataverseDateTime.EnsureUtc(e.GetAttributeValue<DateTime>("createdon"))
            : null;

        // packagehistory has no dedicated end-time field — modifiedon is the only proxy.
        // Only treat it as a completion time when the record is terminal (Completed or Failed).
        // For InProcess records modifiedon reflects the last interim write, not a real end time.
        string? statusLabel = StatusLabel();
        bool isTerminal = !string.Equals(statusLabel, "In Process", StringComparison.OrdinalIgnoreCase);
        DateTime? end = isTerminal && e.Contains("modifiedon")
            ? DataverseDateTime.EnsureUtc(e.GetAttributeValue<DateTime>("modifiedon"))
            : null;

        Guid? operationId = null;
        if (e.Contains("operationid"))
        {
            var raw = e["operationid"];
            if (raw is Guid g)
            {
                operationId = g;
            }
            else if (raw is string s && Guid.TryParse(s, out var parsed))
            {
                operationId = parsed;
            }
        }

        Guid? correlationId = null;
        if (e.Contains("correlationid"))
        {
            var raw = e["correlationid"];
            if (raw is Guid g)
            {
                correlationId = g;
            }
            else if (raw is string s && Guid.TryParse(s, out var parsed))
            {
                correlationId = parsed;
            }
        }

        return new PackageHistoryRecord(
            Id: e.Id,
            Name: e.GetAttributeValue<string>("uniquename") ?? e.GetAttributeValue<string>("executionname"),
            Status: statusLabel,
            Stage: StageLabel(),
            StartedAtUtc: start,
            CompletedAtUtc: end,
            OperationId: operationId,
            Message: e.GetAttributeValue<string>("statusmessage"),
            CorrelationId: correlationId);
    }

}
