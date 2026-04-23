using System.Text.Json;

namespace TALXIS.CLI.Core.Storage;

/// <summary>
/// Shared helpers for atomic JSON read/write used by every file-backed store.
/// Writes go via a sibling <c>*.tmp</c> file and <see cref="File.Replace(string,string,string?)"/>
/// to avoid torn files on crash.
/// </summary>
public static class JsonFile
{
    public static async Task<T> ReadOrDefaultAsync<T>(string path, CancellationToken ct) where T : new()
    {
        if (!File.Exists(path)) return new T();
        await using var stream = File.OpenRead(path);
        var value = await JsonSerializer.DeserializeAsync<T>(stream, TxcJsonOptions.Default, ct).ConfigureAwait(false);
        return value ?? new T();
    }

    public static async Task WriteAtomicAsync<T>(string path, T value, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = path + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, value, TxcJsonOptions.Default, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }

        if (File.Exists(path))
        {
            // File.Replace preserves the destination file's attributes/ACLs.
            File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }
}
