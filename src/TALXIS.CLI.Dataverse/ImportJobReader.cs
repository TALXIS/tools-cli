using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace TALXIS.CLI.Dataverse;

/// <summary>
/// Structured view of a row in the <c>importjob</c> table. All <see cref="DateTime"/> values are UTC.
/// </summary>
public sealed record ImportJobRecord(
    Guid Id,
    string? SolutionName,
    double? Progress,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? Data);

/// <summary>
/// Reader for the <c>importjob</c> table and the
/// <see cref="RetrieveFormattedImportJobResultsRequest"/> SDK message.
/// </summary>
public sealed class ImportJobReader
{
    private const string EntityName = "importjob";
    private static readonly ColumnSet Columns = new(
        "importjobid",
        "solutionname",
        "progress",
        "startedon",
        "completedon",
        "data");

    private readonly IOrganizationServiceAsync2 _service;
    private readonly ILogger? _logger;

    public ImportJobReader(IOrganizationServiceAsync2 service, ILogger? logger = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger;
    }

    /// <summary>
    /// Import jobs whose <c>startedon</c> falls within <paramref name="windowStartUtc"/>..<paramref name="windowEndUtc"/>.
    /// Used for package→solution correlation when no FK is available.
    /// </summary>
    public async Task<IReadOnlyList<ImportJobRecord>> GetInTimeWindowAsync(
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
        q.Criteria.AddCondition("startedon", ConditionOperator.OnOrAfter, start);
        q.Criteria.AddCondition("startedon", ConditionOperator.OnOrBefore, end);
        q.AddOrder("startedon", OrderType.Ascending);

        var res = await _service.RetrieveMultipleAsync(q, ct).ConfigureAwait(false);
        return res.Entities.Select(ToRecord).ToList();
    }

    /// <summary>
    /// Retrieves the formatted (XML) import log via
    /// <see cref="RetrieveFormattedImportJobResultsRequest"/>.
    /// </summary>
    public async Task<string> GetFormattedResultsAsync(Guid importJobId, CancellationToken ct = default)
    {
        var request = new RetrieveFormattedImportJobResultsRequest
        {
            ImportJobId = importJobId,
        };
        var response = (RetrieveFormattedImportJobResultsResponse)await _service
            .ExecuteAsync(request, ct)
            .ConfigureAwait(false);
        return response.FormattedResults ?? string.Empty;
    }

    private static ImportJobRecord ToRecord(Entity e)
    {
        double? progress = e.Contains("progress") ? e.GetAttributeValue<double>("progress") : null;

        DateTime? startedOn = e.Contains("startedon")
            ? DataverseDateTime.EnsureUtc(e.GetAttributeValue<DateTime>("startedon"))
            : null;

        DateTime? completedOn = e.Contains("completedon")
            ? DataverseDateTime.EnsureUtc(e.GetAttributeValue<DateTime>("completedon"))
            : null;

        return new ImportJobRecord(
            Id: e.Id,
            SolutionName: e.GetAttributeValue<string>("solutionname"),
            Progress: progress,
            StartedAtUtc: startedOn,
            CompletedAtUtc: completedOn,
            Data: e.GetAttributeValue<string>("data"));
    }
}
