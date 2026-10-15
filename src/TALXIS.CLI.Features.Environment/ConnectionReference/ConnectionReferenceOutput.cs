using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Features.Environment.ConnectionReference;

/// <summary>
/// What <c>connection-reference create</c> and <c>update</c> print: the outcome
/// <c>status</c> followed by the fields of <c>connection-reference get</c>, under the same names.
/// </summary>
internal sealed record ConnectionReferenceResult(
    string Status,
    Guid Id,
    string LogicalName,
    string? DisplayName,
    string ApiId,
    string? ConnectionId,
    string BindingStatus,
    string? BindingMessage,
    bool IsManaged,
    string? Owner)
{
    public static ConnectionReferenceResult From(ConnectionReferenceOutcome outcome)
    {
        var r = outcome.Reference;
        return new ConnectionReferenceResult(
            outcome.Status.ToKebabCase(), r.Id, r.LogicalName, r.DisplayName, r.ApiId, r.ConnectionId,
            r.BindingStatus, r.BindingMessage, r.IsManaged, r.Owner);
    }

    /// <summary>One line for text output.</summary>
    public string Describe()
        => ConnectionId is null
            ? $"Connection reference '{LogicalName}' {Status}; it is not bound to a connection."
            : $"Connection reference '{LogicalName}' {Status}; bound to connection {ConnectionId} ({BindingStatus}).";
}
