using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Application;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace TALXIS.CLI.Platform.Dataverse.Application.Sdk;

/// <summary>
/// Reader for the standard <c>plugintracelog</c> table. Because it is a normal
/// table (not a virtual one), every filter is pushed server-side via
/// <see cref="QueryExpression"/> conditions.
/// </summary>
public sealed class PluginTraceLogReader
{
    private const string EntityName = DataverseSchema.PluginTraceLog.EntityName;
    private static readonly ColumnSet Columns = new(
        DataverseSchema.PluginTraceLog.PluginTraceLogId,
        DataverseSchema.PluginTraceLog.CreatedOn,
        DataverseSchema.PluginTraceLog.TypeName,
        DataverseSchema.PluginTraceLog.MessageName,
        DataverseSchema.PluginTraceLog.PrimaryEntity,
        DataverseSchema.PluginTraceLog.Mode,
        DataverseSchema.PluginTraceLog.Depth,
        DataverseSchema.PluginTraceLog.OperationType,
        DataverseSchema.PluginTraceLog.ExceptionDetails,
        DataverseSchema.PluginTraceLog.MessageBlock,
        DataverseSchema.PluginTraceLog.CorrelationId,
        DataverseSchema.PluginTraceLog.PerformanceExecutionDuration);

    private readonly IOrganizationServiceAsync2 _service;
    private readonly ILogger? _logger;

    public PluginTraceLogReader(IOrganizationServiceAsync2 service, ILogger? logger = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger;
    }

    /// <summary>
    /// Returns recent plug-in trace rows ordered by <c>createdon</c> descending,
    /// applying every condition in <paramref name="filter"/> server-side.
    /// </summary>
    public async Task<IReadOnlyList<PluginTraceRecord>> GetRecentAsync(
        EnvironmentLogFilter filter,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var q = new QueryExpression(EntityName)
        {
            ColumnSet = Columns,
            Criteria = new FilterExpression(LogicalOperator.And),
            TopCount = filter.Top > 0 ? filter.Top : 50,
        };
        q.AddOrder(DataverseSchema.PluginTraceLog.CreatedOn, OrderType.Descending);

        if (filter.SinceUtc is { } since)
        {
            q.Criteria.AddCondition(
                DataverseSchema.PluginTraceLog.CreatedOn,
                ConditionOperator.GreaterEqual,
                DataverseDateTime.EnsureUtc(since));
        }

        if (!string.IsNullOrWhiteSpace(filter.Plugin))
        {
            q.Criteria.AddCondition(
                DataverseSchema.PluginTraceLog.TypeName,
                ConditionOperator.Like,
                $"%{filter.Plugin.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(filter.Entity))
        {
            q.Criteria.AddCondition(
                DataverseSchema.PluginTraceLog.PrimaryEntity,
                ConditionOperator.Equal,
                filter.Entity.Trim().ToLowerInvariant());
        }

        if (filter.ErrorsOnly)
        {
            q.Criteria.AddCondition(
                DataverseSchema.PluginTraceLog.ExceptionDetails,
                ConditionOperator.NotNull);
        }

        if (filter.CorrelationId is { } correlationId)
        {
            q.Criteria.AddCondition(
                DataverseSchema.PluginTraceLog.CorrelationId,
                ConditionOperator.Equal,
                correlationId);
        }

        var res = await _service.RetrieveMultipleAsync(q, ct).ConfigureAwait(false);
        return res.Entities.Select(ToRecord).ToList();
    }

    internal static PluginTraceRecord ToRecord(Entity e)
    {
        DateTime? createdOn = e.Contains(DataverseSchema.PluginTraceLog.CreatedOn)
            ? DataverseDateTime.EnsureUtc(e.GetAttributeValue<DateTime>(DataverseSchema.PluginTraceLog.CreatedOn))
            : null;

        string? mode = e.FormattedValues.TryGetValue(DataverseSchema.PluginTraceLog.Mode, out var modeFmt)
            && !string.IsNullOrWhiteSpace(modeFmt)
                ? modeFmt
                : e.GetAttributeValue<OptionSetValue>(DataverseSchema.PluginTraceLog.Mode)?.Value.ToString();

        int? depth = e.Contains(DataverseSchema.PluginTraceLog.Depth)
            ? e.GetAttributeValue<int>(DataverseSchema.PluginTraceLog.Depth)
            : null;

        long? duration = e.Contains(DataverseSchema.PluginTraceLog.PerformanceExecutionDuration)
            ? e.GetAttributeValue<int>(DataverseSchema.PluginTraceLog.PerformanceExecutionDuration)
            : null;

        var exception = e.GetAttributeValue<string>(DataverseSchema.PluginTraceLog.ExceptionDetails);
        var message = e.GetAttributeValue<string>(DataverseSchema.PluginTraceLog.MessageBlock);

        Guid? correlationId = DataverseEntityRead.ReadGuid(e, DataverseSchema.PluginTraceLog.CorrelationId);

        return new PluginTraceRecord(
            Id: e.Id,
            CreatedOnUtc: createdOn,
            TypeName: e.GetAttributeValue<string>(DataverseSchema.PluginTraceLog.TypeName),
            MessageName: e.GetAttributeValue<string>(DataverseSchema.PluginTraceLog.MessageName),
            PrimaryEntity: e.GetAttributeValue<string>(DataverseSchema.PluginTraceLog.PrimaryEntity),
            Mode: mode,
            Depth: depth,
            DurationMs: duration,
            HasException: !string.IsNullOrWhiteSpace(exception),
            ExceptionSnippet: Snippet(exception),
            MessageSnippet: Snippet(message),
            CorrelationId: correlationId);
    }

    /// <summary>
    /// Collapses a multi-line trace/exception block to a single trimmed line capped
    /// at 200 characters, so it fits a table cell and a JSON summary alike.
    /// </summary>
    internal static string? Snippet(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var oneLine = value.Replace("\r", " ").Replace("\n", " ").Trim();
        while (oneLine.Contains("  ", StringComparison.Ordinal))
            oneLine = oneLine.Replace("  ", " ", StringComparison.Ordinal);
        return oneLine.Length > 200 ? oneLine[..199] + "…" : oneLine;
    }
}
