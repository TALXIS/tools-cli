using Microsoft.Xrm.Sdk;

namespace Microsoft.Xrm.Tooling.Connector;

/// <summary>
/// Shim for the legacy <c>Microsoft.Xrm.Tooling.Connector.BatchItemOrganizationRequest</c>.
/// CMT's <c>ImportEntityDataBatchMode</c> accesses <c>RequestBatch.BatchItems</c>
/// which returns <c>List&lt;BatchItemOrganizationRequest&gt;</c>. This type mirrors
/// the modern <see cref="Microsoft.PowerPlatform.Dataverse.Client.BatchItemOrganizationRequest"/>.
/// </summary>
public class BatchItemOrganizationRequest
{
    /// <summary>Organization Service request for the batch.</summary>
    public OrganizationRequest? Request { get; set; }

    /// <summary>Request debug message.</summary>
    public string? RequestDebugMessage { get; set; }

    /// <summary>Reference Correlation ID.</summary>
    public Guid RequestReferenceNumber { get; set; }

    /// <summary>
    /// Wraps a modern <see cref="Microsoft.PowerPlatform.Dataverse.Client.BatchItemOrganizationRequest"/>.
    /// </summary>
    internal static BatchItemOrganizationRequest From(Microsoft.PowerPlatform.Dataverse.Client.BatchItemOrganizationRequest modern)
    {
        return new BatchItemOrganizationRequest
        {
            Request = modern.Request,
            RequestDebugMessage = modern.RequestDebugMessage,
            RequestReferenceNumber = modern.RequestReferenceNumber,
        };
    }
}
