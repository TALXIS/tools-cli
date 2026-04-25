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
    private readonly string _changesetDir;
    private readonly string _changesetFile;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        // Ensure Dictionary<string, object?> values round-trip correctly
        Converters = { new ObjectToInferredTypesConverter() }
    };

    public InMemoryChangesetStore()
    {
        _changesetDir = Path.Combine(Environment.CurrentDirectory, ".txc");
        _changesetFile = Path.Combine(_changesetDir, "changeset.json");
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
            if (File.Exists(_changesetFile))
            {
                var json = File.ReadAllText(_changesetFile);
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
            Directory.CreateDirectory(_changesetDir);
            var json = JsonSerializer.Serialize(_operations, SerializerOptions);
            File.WriteAllText(_changesetFile, json);
        }
        catch (Exception ex)
        {
            // Log warning so user knows persistence failed
            Console.Error.WriteLine($"Warning: Failed to persist changeset to disk: {ex.Message}");
            Console.Error.WriteLine("Staged operations are in memory only and will be lost when the process exits.");
        }
    }

    private void DeleteFromDisk()
    {
        try
        {
            if (File.Exists(_changesetFile))
                File.Delete(_changesetFile);
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
