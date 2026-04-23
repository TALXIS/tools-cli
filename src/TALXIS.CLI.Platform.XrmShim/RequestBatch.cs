using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;

namespace Microsoft.Xrm.Tooling.Connector;

/// <summary>
/// Shim for the legacy <c>Microsoft.Xrm.Tooling.Connector.RequestBatch</c> type.
/// CMT's <c>ImportEntityDataBatchMode</c> calls
/// <c>CrmServiceClient.GetBatchById(Guid)</c> and accesses properties on the
/// returned <c>RequestBatch</c>.
/// This wraps the modern <see cref="Microsoft.PowerPlatform.Dataverse.Client.RequestBatch"/>
/// to satisfy the legacy type reference.
/// </summary>
public class RequestBatch
{
    private readonly Microsoft.PowerPlatform.Dataverse.Client.RequestBatch? _inner;

    internal RequestBatch(Microsoft.PowerPlatform.Dataverse.Client.RequestBatch? inner)
    {
        _inner = inner;
    }

    public RequestBatch(string batchName, bool returnResponses = true, bool continueOnError = false)
    {
        _inner = new Microsoft.PowerPlatform.Dataverse.Client.RequestBatch(batchName, returnResponses, continueOnError);
    }

    /// <summary>ID of the batch.</summary>
    public Guid BatchId => _inner?.BatchId ?? Guid.Empty;

    /// <summary>Display name of the batch.</summary>
    public string BatchName => _inner?.BatchName ?? string.Empty;

    /// <summary>Items to execute.</summary>
    public List<BatchItemOrganizationRequest> BatchItems =>
        _inner?.BatchItems?.Select(BatchItemOrganizationRequest.From).ToList()
        ?? new List<BatchItemOrganizationRequest>();

    /// <summary>Settings for this Execute Multiple Request.</summary>
    public ExecuteMultipleSettings? BatchRequestSettings => _inner?.BatchRequestSettings;

    /// <summary>Results from the batch.</summary>
    public ExecuteMultipleResponse? BatchResults => _inner?.BatchResults;

    /// <summary>Status of the batch.</summary>
    public Microsoft.PowerPlatform.Dataverse.Client.BatchStatus Status =>
        _inner?.Status ?? Microsoft.PowerPlatform.Dataverse.Client.BatchStatus.Waiting;

    /// <summary>Returns the wrapped modern RequestBatch.</summary>
    internal Microsoft.PowerPlatform.Dataverse.Client.RequestBatch? Inner => _inner;
}
