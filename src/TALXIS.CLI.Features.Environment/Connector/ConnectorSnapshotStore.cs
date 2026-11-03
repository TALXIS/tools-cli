using System.Text.Json;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Core.Storage;

namespace TALXIS.CLI.Features.Environment.Connector;

/// <summary>
/// Fetch-once copies of connector metadata in the workspace, under
/// <c>.txc/connectors/&lt;connector&gt;/</c>: <c>index.json</c> for the operation index,
/// <c>&lt;operation&gt;.json</c> for one operation. They are meant to be committed, so a
/// flow's review shows the contract it was written against, and later commands
/// (on any machine, or in CI) read them instead of the environment.
/// </summary>
/// <remarks>
/// Only the parsed metadata is stored: no runtime URL, SAS link, creator or
/// environment, since those differ per environment and go stale. A file with
/// another schema version is ignored and rewritten on the next fetch.
/// </remarks>
internal sealed class ConnectorSnapshotStore
{
    public const int SchemaVersion = 1;
    public const string DirectoryName = "connectors";
    private const string IndexFileName = "index.json";

    /// <summary>
    /// Storage options without the dictionary key policy: resolver argument
    /// names (<c>$select</c>) and value labels are data and must round-trip as is.
    /// </summary>
    private static readonly JsonSerializerOptions Options = new(TxcJsonOptions.Default) { DictionaryKeyPolicy = null };

    private readonly string _workspaceRoot;

    public ConnectorSnapshotStore(string workspaceRoot)
    {
        _workspaceRoot = workspaceRoot;
    }

    /// <summary>The snapshot directory, <c>&lt;root&gt;/.txc/connectors</c>.</summary>
    public string Root => Path.Combine(_workspaceRoot, Core.Resolution.WorkspaceDiscovery.DirectoryName, DirectoryName);

    public async Task<OperationDetail?> ReadOperationAsync(string connector, string operation, CancellationToken ct)
    {
        var path = Find(connector, operation + ".json");
        var snapshot = path is null ? null : await ReadAsync<OperationSnapshot>(path, ct).ConfigureAwait(false);
        return snapshot is { SchemaVersion: SchemaVersion, Operation: { } detail }
            ? detail with { Source = ConnectorMetadataSource.Snapshot, SourcePath = Relative(path!) }
            : null;
    }

    public async Task<ConnectorDetail?> ReadConnectorAsync(string connector, CancellationToken ct)
    {
        var path = Find(connector, IndexFileName);
        var snapshot = path is null ? null : await ReadAsync<ConnectorSnapshot>(path, ct).ConfigureAwait(false);
        return snapshot is { SchemaVersion: SchemaVersion, Connector: { } detail }
            ? detail with { Source = ConnectorMetadataSource.Snapshot, SourcePath = Relative(path!) }
            : null;
    }

    /// <summary>Writes the operation under the connector it was fetched as; returns the file, relative to the workspace.</summary>
    public async Task<string> WriteOperationAsync(OperationDetail operation, DateTimeOffset fetchedAt, CancellationToken ct)
    {
        var path = Path.Combine(Root, FileName(operation.Connector), FileName(operation.OperationId) + ".json");
        var stored = operation with { Source = null, SourcePath = null };
        await WriteAtomicAsync(path, new OperationSnapshot(SchemaVersion, fetchedAt, stored), ct).ConfigureAwait(false);
        return Relative(path);
    }

    public async Task<string> WriteConnectorAsync(ConnectorDetail connector, DateTimeOffset fetchedAt, CancellationToken ct)
    {
        var path = Path.Combine(Root, FileName(connector.LogicalName), IndexFileName);
        var stored = connector with { Source = null, SourcePath = null, MatchedOperations = null };
        await WriteAtomicAsync(path, new ConnectorSnapshot(SchemaVersion, fetchedAt, stored), ct).ConfigureAwait(false);
        return Relative(path);
    }

    /// <summary>
    /// The snapshot file for <paramref name="connector"/>, matching the
    /// directory and file names without regard to case, as connector and
    /// operation ids are.
    /// </summary>
    private string? Find(string connector, string fileName)
    {
        if (!Directory.Exists(Root))
            return null;

        var directory = Directory.EnumerateDirectories(Root)
            .FirstOrDefault(d => string.Equals(Path.GetFileName(d), FileName(connector), StringComparison.OrdinalIgnoreCase));
        return directory is null
            ? null
            : Directory.EnumerateFiles(directory, "*.json")
                .FirstOrDefault(f => string.Equals(Path.GetFileName(f), FileName(Path.GetFileNameWithoutExtension(fileName)) + ".json", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<T?> ReadAsync<T>(string path, CancellationToken ct) where T : class
    {
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, Options, ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // A hand-edited or merge-damaged snapshot is fetched again rather than trusted.
            return null;
        }
    }

    private static async Task WriteAtomicAsync<T>(string path, T value, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temp))
            {
                await JsonSerializer.SerializeAsync(stream, value, Options, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    /// <summary>A connector or operation id as a file name: path separators and invalid characters become <c>_</c>.</summary>
    private static string FileName(string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(ConnectorIds.ToShort(id).Select(c => invalid.Contains(c) || c is '/' or '\\' or ':' ? '_' : c).ToArray());
    }

    private string Relative(string path) => Path.GetRelativePath(_workspaceRoot, path).Replace('\\', '/');

    private sealed record OperationSnapshot(int SchemaVersion, DateTimeOffset FetchedAt, OperationDetail? Operation);

    private sealed record ConnectorSnapshot(int SchemaVersion, DateTimeOffset FetchedAt, ConnectorDetail? Connector);
}
