using Microsoft.Extensions.Logging;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace TALXIS.CLI.Deploy;

public sealed record PackageHistoryStatusCodes(
    int? InProcessStatus,
    int? InProcessState,
    int? SuccessStatus,
    int? SuccessState,
    int? FailedStatus,
    int? FailedState);

/// <summary>
/// Writes packagehistory rows for txc-managed package uninstall runs so they are
/// visible in deploy list/show alongside install runs.
/// </summary>
public sealed class PackageHistoryWriter
{
    private const string EntityName = DeploySchema.PackageHistory.EntityName;
    private readonly IOrganizationServiceAsync2 _service;
    private readonly ILogger? _logger;

    public PackageHistoryWriter(IOrganizationServiceAsync2 service, ILogger? logger = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger;
    }

    public async Task<PackageHistoryStatusCodes> ResolveStatusCodesAsync(CancellationToken ct = default)
    {
        var metadataCodes = await TryResolveStatusCodesFromMetadataAsync(ct).ConfigureAwait(false);
        if (metadataCodes is not null)
        {
            return metadataCodes;
        }

        var query = new QueryExpression(EntityName)
        {
            ColumnSet = new ColumnSet("statecode", "statuscode"),
            TopCount = 200,
            Criteria = new FilterExpression(LogicalOperator.And)
        };
        query.AddOrder("createdon", OrderType.Descending);

        var response = await _service.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        var samples = response.Entities
            .Select(e => (
                Status: e.GetAttributeValue<OptionSetValue>("statuscode")?.Value,
                State: e.GetAttributeValue<OptionSetValue>("statecode")?.Value,
                Label: e.FormattedValues.TryGetValue("statuscode", out var label) ? label : null))
            .Where(x => x.Status.HasValue && x.State.HasValue)
            .Select(x => (x.Status!.Value, x.State!.Value, x.Label));

        return ResolveStatusCodesFromSamples(samples);
    }

    private async Task<PackageHistoryStatusCodes?> TryResolveStatusCodesFromMetadataAsync(CancellationToken ct)
    {
        try
        {
            var request = new RetrieveAttributeRequest
            {
                EntityLogicalName = EntityName,
                LogicalName = "statuscode",
                RetrieveAsIfPublished = true
            };

            var response = await _service.ExecuteAsync(request, ct).ConfigureAwait(false) as RetrieveAttributeResponse;
            if (response?.AttributeMetadata is not StatusAttributeMetadata statusAttribute
                || statusAttribute.OptionSet?.Options is null)
            {
                return null;
            }

            var options = statusAttribute.OptionSet.Options
                .OfType<StatusOptionMetadata>()
                .Where(o => o.Value is not null && o.State is not null)
                .Select(o => (
                    Status: o.Value!.Value,
                    State: o.State!.Value,
                    Label: o.Label?.UserLocalizedLabel?.Label
                        ?? o.Label?.LocalizedLabels?.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l.Label))?.Label))
                .ToList();

            if (options.Count == 0)
            {
                return null;
            }

            return ResolveStatusCodesFromSamples(options);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Unable to resolve packagehistory status codes from metadata.");
            return null;
        }
    }

    internal static PackageHistoryStatusCodes ResolveStatusCodesFromSamples(IEnumerable<(int Status, int State, string? Label)> samples)
    {
        var normalized = samples
            .Select(s => (s.Status, s.State, Label: (s.Label ?? string.Empty).Trim()))
            .Where(s => !string.IsNullOrWhiteSpace(s.Label))
            .ToList();

        if (normalized.Count == 0)
        {
            return new PackageHistoryStatusCodes(
                InProcessStatus: null,
                InProcessState: null,
                SuccessStatus: null,
                SuccessState: null,
                FailedStatus: null,
                FailedState: null);
        }

        var inProcessCandidates = normalized
            .Where(x =>
                x.Label.Contains("in process", StringComparison.OrdinalIgnoreCase)
                || x.Label.Contains("inprogress", StringComparison.OrdinalIgnoreCase)
                || x.Label.Contains("in progress", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var inProcess = inProcessCandidates.Count > 0
            ? (inProcessCandidates[0].Status, inProcessCandidates[0].State)
            : ((int Status, int State)?)null;

        var successCandidates = normalized
            .Where(x =>
                x.Label.Contains("success", StringComparison.OrdinalIgnoreCase)
                || x.Label.Contains("completed", StringComparison.OrdinalIgnoreCase)
                || x.Label.Contains("succeeded", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var success = PickTerminalCandidate(successCandidates, inProcess?.State);

        var failedCandidates = normalized
            .Where(x =>
                x.Label.Contains("fail", StringComparison.OrdinalIgnoreCase)
                || x.Label.Contains("error", StringComparison.OrdinalIgnoreCase)
                || x.Label.Contains("cancel", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var failed = PickTerminalCandidate(failedCandidates, inProcess?.State);

        return new PackageHistoryStatusCodes(
            InProcessStatus: inProcess?.Status,
            InProcessState: inProcess?.State,
            SuccessStatus: success?.Status,
            SuccessState: success?.State,
            FailedStatus: failed?.Status,
            FailedState: failed?.State);
    }

    private static (int Status, int State)? PickTerminalCandidate(
        IReadOnlyList<(int Status, int State, string Label)> candidates,
        int? inProcessState)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        if (inProcessState is { } activeState)
        {
            foreach (var candidate in candidates)
            {
                if (candidate.State != activeState)
                {
                    return (candidate.Status, candidate.State);
                }
            }
        }

        var first = candidates[0];
        return (first.Status, first.State);
    }

    public async Task<PackageHistoryRecord?> TryCreateUninstallRunAsync(
        string uniqueName,
        string executionName,
        int? statusCode,
        string? message,
        CancellationToken ct = default)
    {
        try
        {
            var entity = new Entity(EntityName)
            {
                ["uniquename"] = uniqueName,
                ["executionname"] = executionName,
            };
            if (!string.IsNullOrWhiteSpace(message))
            {
                entity["statusmessage"] = message;
            }
            if (statusCode is { } sc)
            {
                entity["statuscode"] = new OptionSetValue(sc);
            }

            var id = await _service.CreateAsync(entity, ct).ConfigureAwait(false);
            var reader = new PackageHistoryReader(_service, _logger);
            return await reader.GetByIdAsync(id, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Unable to create packagehistory uninstall run record.");
            return null;
        }
    }

    public async Task TryUpdateStatusAsync(
        Guid packageHistoryId,
        int? stateCode,
        int? statusCode,
        string? message,
        CancellationToken ct = default)
    {
        try
        {
            if (stateCode is { } st && statusCode is { } sc)
            {
                var setState = new SetStateRequest
                {
                    EntityMoniker = new EntityReference(EntityName, packageHistoryId),
                    State = new OptionSetValue(st),
                    Status = new OptionSetValue(sc)
                };
                await _service.ExecuteAsync(setState, ct).ConfigureAwait(false);
            }

            var entity = new Entity(EntityName, packageHistoryId);
            if (!string.IsNullOrWhiteSpace(message))
            {
                entity["statusmessage"] = message;
            }
            if (entity.Attributes.Count > 0)
            {
                await _service.UpdateAsync(entity, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Unable to update packagehistory uninstall run record {PackageHistoryId}.", packageHistoryId);
        }
    }
}
