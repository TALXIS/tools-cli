namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Represents a single operation staged in a changeset.
/// </summary>
public sealed record StagedOperation
{
    /// <summary>Sequential index within the changeset.</summary>
    public int Index { get; init; }

    /// <summary>"schema" or "data"</summary>
    public required string Category { get; init; }

    /// <summary>"CREATE", "UPDATE", "DELETE", "ASSOCIATE", "DISASSOCIATE", "UPLOAD"</summary>
    public required string OperationType { get; init; }

    /// <summary>Target type: "entity", "attribute", "relationship", "optionset", "record", "file"</summary>
    public required string TargetType { get; init; }

    /// <summary>Human-readable target description (e.g., "fin_customer", "fin_customer.fin_email")</summary>
    public required string TargetDescription { get; init; }

    /// <summary>Human-readable details (e.g., "type: string, format: email")</summary>
    public string? Details { get; init; }

    /// <summary>
    /// Serializable parameters for the operation. The changeset applier
    /// uses these to reconstruct the SDK/API request.
    /// </summary>
    public required Dictionary<string, object?> Parameters { get; init; }

    /// <summary>Timestamp when the operation was staged.</summary>
    public DateTimeOffset StagedAt { get; init; } = DateTimeOffset.UtcNow;
}
