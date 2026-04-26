using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Runtime;
using TALXIS.CLI.Platform.Dataverse.Application;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace TALXIS.CLI.Platform.Dataverse.Application.Sdk;

/// <summary>
/// Uninstalls solutions from Dataverse by unique name.
/// Mirrors PAC's lookup+delete model: find by <c>solution.uniquename</c>, then issue delete.
/// </summary>
public sealed class SolutionUninstaller
{
    private const string EntityName = DataverseSchema.Solution.EntityName;
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

    /// <summary>
    /// Deletes a solution by unique name. When <paramref name="expectManaged"/>
    /// is specified, rejects solutions that don't match the expected type.
    /// </summary>
    /// <param name="uniqueName">Solution unique name.</param>
    /// <param name="expectManaged">
    /// <c>true</c> = expect managed (uninstall), <c>false</c> = expect unmanaged (delete),
    /// <c>null</c> = no type check (legacy behavior).
    /// </param>
    public async Task<SolutionUninstallOutcome> UninstallByUniqueNameAsync(
        string uniqueName,
        bool? expectManaged = null,
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
        var isManaged = target.GetAttributeValue<bool>("ismanaged");

        // Type check: reject mismatched solution types with actionable guidance.
        if (expectManaged.HasValue && isManaged != expectManaged.Value)
        {
            var message = isManaged
                ? $"Solution '{trimmed}' is managed. Use 'txc env sln uninstall' to remove it and all its components."
                : $"Solution '{trimmed}' is unmanaged. Use 'txc env sln delete' to remove the solution container (components will remain).";
            return new SolutionUninstallOutcome(trimmed, target.Id, SolutionUninstallStatus.TypeMismatch, message);
        }

        try
        {
            var request = new DeleteRequest
            {
                Target = target.ToEntityReference(),
                RequestId = Guid.NewGuid(),
            };
            await _service.ExecuteAsync(request, ct).ConfigureAwait(false);

            var successMessage = isManaged
                ? "Uninstalled. All components from this managed solution have been removed."
                : "Deleted. The solution container has been removed; components remain in the environment.";
            return new SolutionUninstallOutcome(trimmed, target.Id, SolutionUninstallStatus.Success, successMessage);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to delete solution {SolutionName}.", trimmed);
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
            outcomes.Add(await UninstallByUniqueNameAsync(name, expectManaged: null, ct).ConfigureAwait(false));
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
