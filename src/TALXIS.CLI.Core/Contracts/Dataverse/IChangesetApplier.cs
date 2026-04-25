namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Result of applying a changeset against a live Dataverse environment.
/// </summary>
public record ChangesetApplyResult
{
    public int TotalOperations { get; init; }
    public int Succeeded { get; init; }
    public int Failed { get; init; }
    public int Skipped { get; init; }
    public int RolledBack { get; init; }
    public TimeSpan Duration { get; init; }
    public List<OperationResult> Results { get; init; } = new();
}

/// <summary>
/// Outcome of a single operation within a changeset apply run.
/// </summary>
public record OperationResult(int Index, bool Success, string Message, string? Error = null);

/// <summary>
/// Applies a set of staged changeset operations against a Dataverse environment
/// using the requested execution strategy (batch, transaction, or bulk).
/// </summary>
public interface IChangesetApplier
{
    Task<ChangesetApplyResult> ApplyAsync(
        string? profileName,
        IReadOnlyList<StagedOperation> operations,
        string strategy,
        bool continueOnError,
        CancellationToken ct);
}
