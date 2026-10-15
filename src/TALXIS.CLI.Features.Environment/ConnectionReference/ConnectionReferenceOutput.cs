using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Features.Environment.ConnectionReference;

/// <summary>Output shared by the create and update commands.</summary>
internal static class ConnectionReferenceOutput
{
    public static void Write(ConnectionReferenceOutcome outcome)
    {
        var r = outcome.Reference;
        OutputFormatter.WriteData(
            new
            {
                status = outcome.Status,
                logicalName = r.LogicalName,
                displayName = r.DisplayName,
                apiId = r.ApiId,
                connectionId = r.ConnectionId,
                bindingStatus = r.BindingStatus,
                bindingMessage = r.BindingMessage,
            },
            _ =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine(r.ConnectionId is null
                    ? $"Connection reference '{r.LogicalName}' {outcome.Status}; it is not bound to a connection."
                    : $"Connection reference '{r.LogicalName}' {outcome.Status}; bound to connection {r.ConnectionId} ({r.BindingStatus}).");
#pragma warning restore TXC003
            });
    }
}
