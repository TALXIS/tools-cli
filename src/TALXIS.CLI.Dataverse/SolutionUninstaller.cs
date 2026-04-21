using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace TALXIS.CLI.Dataverse;

public enum SolutionUninstallStatus
{
    Success,
    NotFound,
    Ambiguous,
    Failed,
}

public sealed record SolutionUninstallOutcome(
    string SolutionName,
    Guid? SolutionId,
    SolutionUninstallStatus Status,
    string Message);

/// <summary>
/// Uninstalls solutions from Dataverse by unique name.
/// Mirrors PAC's lookup+delete model: find by <c>solution.uniquename</c>, then issue delete.
/// </summary>
public sealed class SolutionUninstaller
{
    private const string EntityName = "solution";
    private static readonly ColumnSet Columns = new(
        "solutionid",
        "uniquename",
        "friendlyname",
        "version",
        "ismanaged");

    private readonly IOrganizationServiceAsync2 _service;
    private readonly ILogger? _logger;

    public SolutionUninstaller(IOrganizationServiceAsync2 service, ILogger? logger = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger;
    }

    public async Task<SolutionUninstallOutcome> UninstallByUniqueNameAsync(
        string uniqueName,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uniqueName);
        var trimmed = uniqueName.Trim();

        var matches = await FindByUniqueNameAsync(trimmed, ct).ConfigureAwait(false);
        if (matches.Count == 0)
        {
            return new SolutionUninstallOutcome(trimmed, null, SolutionUninstallStatus.NotFound, "Solution not found.");
        }

        if (matches.Count > 1)
        {
            return new SolutionUninstallOutcome(trimmed, null, SolutionUninstallStatus.Ambiguous, "Multiple solution rows matched the same unique name.");
        }

        var target = matches[0];
        try
        {
            var request = new DeleteRequest
            {
                Target = target.ToEntityReference(),
                RequestId = Guid.NewGuid(),
            };
            await _service.ExecuteAsync(request, ct).ConfigureAwait(false);
            return new SolutionUninstallOutcome(trimmed, target.Id, SolutionUninstallStatus.Success, "Uninstalled.");
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to uninstall solution {SolutionName}.", trimmed);
            return new SolutionUninstallOutcome(trimmed, target.Id, SolutionUninstallStatus.Failed, ex.Message);
        }
    }

    public async Task<IReadOnlyList<SolutionUninstallOutcome>> UninstallByUniqueNamesAsync(
        IEnumerable<string> uniqueNames,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(uniqueNames);

        var distinct = uniqueNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var outcomes = new List<SolutionUninstallOutcome>(distinct.Count);
        foreach (var name in distinct)
        {
            outcomes.Add(await UninstallByUniqueNameAsync(name, ct).ConfigureAwait(false));
        }

        return outcomes;
    }

    private async Task<IReadOnlyList<Entity>> FindByUniqueNameAsync(string uniqueName, CancellationToken ct)
    {
        var query = new QueryExpression(EntityName)
        {
            ColumnSet = Columns,
            Criteria = new FilterExpression(LogicalOperator.And),
            TopCount = 5,
        };
        query.Criteria.AddCondition("uniquename", ConditionOperator.Equal, uniqueName);
        var response = await _service.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return response.Entities.ToList();
    }
}
