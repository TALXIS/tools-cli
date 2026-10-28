namespace TALXIS.CLI.Features.Environment.Connector.Operation;

/// <summary>A connection reference as far as choosing one goes.</summary>
/// <param name="ConnectionId">The connection it is bound to and usable through; null when unbound or broken.</param>
/// <param name="Owner">Who the connection signs in as.</param>
internal sealed record ReferenceCandidate(string LogicalName, string? DisplayName, string? ConnectionId, string? Owner = null, string? Status = null);

/// <summary>
/// The reference to read through; otherwise a question for the user
/// (<see cref="Status"/> with <see cref="Message"/>, and the bound references
/// to choose from for <see cref="DynamicValuesStatus.NeedsChoice"/>), or an input error.
/// </summary>
internal sealed record ReferencePick(
    ReferenceCandidate? Reference,
    string? InputError = null,
    string? Status = null,
    string? Message = null,
    IReadOnlyList<ReferenceCandidate>? Bound = null);

/// <summary>
/// Picks the connection reference a dynamic lookup reads through: the one
/// the user named, or the only bound one; otherwise says what to ask.
/// </summary>
internal static class ConnectionReferencePicker
{
    public static ReferencePick Pick(IReadOnlyList<ReferenceCandidate> references, string connector, string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var named = references.FirstOrDefault(r => string.Equals(r.LogicalName, requested, StringComparison.OrdinalIgnoreCase));
            if (named is null)
                return new(null, InputError: $"Connection reference '{requested}' for '{connector}' does not exist. Available: {(references.Count == 0 ? "none" : string.Join(", ", references.Select(r => r.LogicalName)))}.");
            if (string.IsNullOrWhiteSpace(named.ConnectionId))
                return new(null, Status: DynamicValuesStatus.NeedsConnection,
                    Message: $"Connection reference '{named.LogicalName}' is not bound to a connection. Ask the user to bind one, then run this again.");
            return new(named);
        }

        var bound = references.Where(r => !string.IsNullOrWhiteSpace(r.ConnectionId)).ToList();
        return bound.Count switch
        {
            1 => new(bound[0]),
            0 => new(null, Status: DynamicValuesStatus.NeedsConnection,
                Message: $"No connection reference for '{connector}' is bound to a connection. Ask the user to create one and bind it to a connection, then run this again. Don't create a connection without their agreement."),
            _ => new(null, Status: DynamicValuesStatus.NeedsChoice,
                Message: $"'{connector}' has {bound.Count} bound connection references (listed below). " +
                    "Ask the user which one the flow should use, then run this again with --connection-reference <logical name>.",
                Bound: bound),
        };
    }
}
