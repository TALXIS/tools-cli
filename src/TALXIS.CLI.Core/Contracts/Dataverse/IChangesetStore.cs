namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Stores staged operations for batch apply.
/// </summary>
public interface IChangesetStore
{
    /// <summary>Add an operation to the changeset.</summary>
    void Add(StagedOperation operation);

    /// <summary>Get all staged operations in order.</summary>
    IReadOnlyList<StagedOperation> GetAll();

    /// <summary>Get operations by category ("schema" or "data").</summary>
    IReadOnlyList<StagedOperation> GetByCategory(string category);

    /// <summary>Get the total count of staged operations.</summary>
    int Count { get; }

    /// <summary>Clear all staged operations.</summary>
    void Clear();
}
