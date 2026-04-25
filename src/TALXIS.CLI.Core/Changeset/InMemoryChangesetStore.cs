using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Core.Changeset;

/// <summary>
/// In-memory changeset store. Used by both CLI sessions and MCP server.
/// Thread-safe for concurrent MCP tool calls.
/// </summary>
public sealed class InMemoryChangesetStore : IChangesetStore
{
    private readonly List<StagedOperation> _operations = new();
    private readonly object _lock = new();
    private int _nextIndex = 1;

    public void Add(StagedOperation operation)
    {
        lock (_lock)
        {
            var indexed = operation with { Index = _nextIndex++ };
            _operations.Add(indexed);
        }
    }

    public IReadOnlyList<StagedOperation> GetAll()
    {
        lock (_lock) { return _operations.ToList().AsReadOnly(); }
    }

    public IReadOnlyList<StagedOperation> GetByCategory(string category)
    {
        lock (_lock) { return _operations.Where(o => o.Category == category).ToList().AsReadOnly(); }
    }

    public int Count { get { lock (_lock) { return _operations.Count; } } }

    public void Clear()
    {
        lock (_lock) { _operations.Clear(); _nextIndex = 1; }
    }
}
