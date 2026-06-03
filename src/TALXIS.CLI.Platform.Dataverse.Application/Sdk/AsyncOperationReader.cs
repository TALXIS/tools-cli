using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Application;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace TALXIS.CLI.Platform.Dataverse.Application.Sdk;

/// <summary>
/// Reader for the standard <c>asyncoperation</c> table (background system jobs,
/// including classic workflow runs). Time, status, correlation and operation-type
/// filters are pushed server-side; the entity filter is client-side because
/// <c>regardingobjectid</c> is a polymorphic lookup with no cleanly filterable
/// entity-name column.
/// </summary>
public sealed class AsyncOperationReader
{
    private const string EntityName = DataverseSchema.AsyncOperation.EntityName;
    private static readonly ColumnSet Columns = new(
        DataverseSchema.AsyncOperation.AsyncOperationId,
        DataverseSchema.AsyncOperation.Name,
        DataverseSchema.AsyncOperation.OperationType,
        DataverseSchema.AsyncOperation.StatusCode,
        DataverseSchema.AsyncOperation.Message,
        DataverseSchema.AsyncOperation.FriendlyMessage,
        DataverseSchema.AsyncOperation.RegardingObjectId,
        DataverseSchema.AsyncOperation.CreatedOn,
        DataverseSchema.AsyncOperation.StartedOn,
        DataverseSchema.AsyncOperation.CompletedOn,
        DataverseSchema.AsyncOperation.CorrelationId,
        DataverseSchema.AsyncOperation.ErrorCode);

    private readonly IOrganizationServiceAsync2 _service;
    private readonly ILogger? _logger;

    public AsyncOperationReader(IOrganizationServiceAsync2 service, ILogger? logger = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger;
    }

    /// <summary>
    /// Returns recent async-operation rows ordered by <c>createdon</c> descending,
    /// applying <paramref name="filter"/> (and the optional
    /// <paramref name="operationTypeFilter"/>) server-side where possible.
    /// </summary>
    public async Task<IReadOnlyList<AsyncJobRecord>> GetRecentAsync(
        EnvironmentLogFilter filter,
        int? operationTypeFilter = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        int top = filter.Top > 0 ? filter.Top : 50;
        bool clientEntityFilter = !string.IsNullOrWhiteSpace(filter.Entity);

        var q = new QueryExpression(EntityName)
        {
            ColumnSet = Columns,
            Criteria = new FilterExpression(LogicalOperator.And),
            // Over-fetch when the entity filter runs client-side so we still
            // return a full page after narrowing.
            TopCount = clientEntityFilter ? Math.Max(top * 4, 50) : top,
        };
        q.AddOrder(DataverseSchema.AsyncOperation.CreatedOn, OrderType.Descending);

        if (filter.SinceUtc is { } since)
        {
            q.Criteria.AddCondition(
                DataverseSchema.AsyncOperation.CreatedOn,
                ConditionOperator.GreaterEqual,
                DataverseDateTime.EnsureUtc(since));
        }

        if (operationTypeFilter is { } opType)
        {
            q.Criteria.AddCondition(
                DataverseSchema.AsyncOperation.OperationType,
                ConditionOperator.Equal,
                opType);
        }

        if (filter.ErrorsOnly)
        {
            q.Criteria.AddCondition(
                DataverseSchema.AsyncOperation.StatusCode,
                ConditionOperator.In,
                DataverseSchema.AsyncOperation.StatusCodeFailed,
                DataverseSchema.AsyncOperation.StatusCodeCanceled);
        }

        if (filter.CorrelationId is { } correlationId)
        {
            q.Criteria.AddCondition(
                DataverseSchema.AsyncOperation.CorrelationId,
                ConditionOperator.Equal,
                correlationId);
        }

        var res = await _service.RetrieveMultipleAsync(q, ct).ConfigureAwait(false);
        IEnumerable<AsyncJobRecord> records = res.Entities.Select(ToRecord);

        if (clientEntityFilter)
        {
            var entity = filter.Entity!.Trim();
            records = records
                .Where(r => string.Equals(r.RegardingEntity, entity, StringComparison.OrdinalIgnoreCase))
                .Take(top);
        }

        return records.ToList();
    }

    internal static AsyncJobRecord ToRecord(Entity e)
    {
        int? operationType = e.GetAttributeValue<OptionSetValue>(DataverseSchema.AsyncOperation.OperationType)?.Value;
        string? operationTypeLabel =
            e.FormattedValues.TryGetValue(DataverseSchema.AsyncOperation.OperationType, out var opFmt)
            && !string.IsNullOrWhiteSpace(opFmt)
                ? opFmt
                : operationType?.ToString();

        int? statusCode = e.GetAttributeValue<OptionSetValue>(DataverseSchema.AsyncOperation.StatusCode)?.Value;
        string? statusLabel =
            e.FormattedValues.TryGetValue(DataverseSchema.AsyncOperation.StatusCode, out var statusFmt)
            && !string.IsNullOrWhiteSpace(statusFmt)
                ? statusFmt
                : statusCode?.ToString();

        bool isError = statusCode is DataverseSchema.AsyncOperation.StatusCodeFailed
            or DataverseSchema.AsyncOperation.StatusCodeCanceled;

        DateTime? createdOn = ReadUtc(e, DataverseSchema.AsyncOperation.CreatedOn);
        DateTime? startedOn = ReadUtc(e, DataverseSchema.AsyncOperation.StartedOn);
        DateTime? completedOn = ReadUtc(e, DataverseSchema.AsyncOperation.CompletedOn);

        var regarding = e.GetAttributeValue<EntityReference>(DataverseSchema.AsyncOperation.RegardingObjectId);

        Guid? correlationId = DataverseEntityRead.ReadGuid(e, DataverseSchema.AsyncOperation.CorrelationId);

        return new AsyncJobRecord(
            Id: e.Id,
            Name: e.GetAttributeValue<string>(DataverseSchema.AsyncOperation.Name),
            OperationTypeCode: operationType,
            OperationTypeLabel: operationTypeLabel,
            StatusLabel: statusLabel,
            IsError: isError,
            CreatedOnUtc: createdOn,
            StartedOnUtc: startedOn,
            CompletedOnUtc: completedOn,
            RegardingEntity: regarding?.LogicalName,
            Message: e.GetAttributeValue<string>(DataverseSchema.AsyncOperation.Message)
                ?? e.GetAttributeValue<string>(DataverseSchema.AsyncOperation.FriendlyMessage),
            CorrelationId: correlationId);
    }

    private static DateTime? ReadUtc(Entity e, string attribute) =>
        e.Contains(attribute)
            ? DataverseDateTime.EnsureUtc(e.GetAttributeValue<DateTime>(attribute))
            : null;
}
