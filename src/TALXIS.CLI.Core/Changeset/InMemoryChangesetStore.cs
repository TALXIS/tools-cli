using System.Text.Json;
using System.Text.Json.Serialization;
using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Core.Changeset;

/// <summary>
/// In-memory changeset store with file-based persistence at .txc/changeset.json.
/// Used by both CLI sessions and MCP server.
/// Thread-safe for concurrent MCP tool calls.
/// Persists operations to disk so changesets survive across CLI invocations.
/// </summary>
public sealed class InMemoryChangesetStore : IChangesetStore
{
    private readonly List<StagedOperation> _operations = new();
    private readonly object _lock = new();
    private int _nextIndex = 1;
    private static readonly string ChangesetDir = Path.Combine(Environment.CurrentDirectory, ".txc");
    private static readonly string ChangesetFile = Path.Combine(ChangesetDir, "changeset.json");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        // Ensure Dictionary<string, object?> values round-trip correctly
        Converters = { new ObjectToInferredTypesConverter() }
    };

    public InMemoryChangesetStore()
    {
        LoadFromDisk();
    }

    public void Add(StagedOperation operation)
    {
        lock (_lock)
        {
            var indexed = operation with { Index = _nextIndex++ };
            _operations.Add(indexed);
            SaveToDisk();
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
        lock (_lock)
        {
            _operations.Clear();
            _nextIndex = 1;
            DeleteFromDisk();
        }
    }

    private void LoadFromDisk()
    {
        try
        {
            if (File.Exists(ChangesetFile))
            {
                var json = File.ReadAllText(ChangesetFile);
                var ops = JsonSerializer.Deserialize<List<StagedOperation>>(json, SerializerOptions);
                if (ops != null && ops.Count > 0)
                {
                    _operations.AddRange(ops);
                    _nextIndex = _operations.Max(o => o.Index) + 1;
                }
            }
        }
        catch
        {
            // If the file is corrupt, start fresh
        }
    }

    private void SaveToDisk()
    {
        try
        {
            Directory.CreateDirectory(ChangesetDir);
            var json = JsonSerializer.Serialize(_operations, SerializerOptions);
            File.WriteAllText(ChangesetFile, json);
        }
        catch
        {
            // Best-effort — if we can't persist, in-memory still works
        }
    }

    private void DeleteFromDisk()
    {
        try
        {
            if (File.Exists(ChangesetFile))
                File.Delete(ChangesetFile);
        }
        catch { }
    }

    /// <summary>
    /// Custom converter to handle Dictionary&lt;string, object?&gt; deserialization.
    /// System.Text.Json deserializes unknown object values as JsonElement by default;
    /// this converter infers primitive types (string, bool, number, null) instead.
    /// </summary>
    private sealed class ObjectToInferredTypesConverter : JsonConverter<object?>
    {
        public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.True => true,
                JsonTokenType.False => false,
                JsonTokenType.Number when reader.TryGetInt64(out var l) => l,
                JsonTokenType.Number => reader.GetDouble(),
                JsonTokenType.String when reader.TryGetDateTimeOffset(out var dto) => dto,
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Null => null,
                // For arrays/objects, fall back to JsonElement so nothing is lost
                _ => JsonDocument.ParseValue(ref reader).RootElement.Clone()
            };
        }

        public override void Write(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, value?.GetType() ?? typeof(object), options);
        }
    }
}
