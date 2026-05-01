using Microsoft.Extensions.Logging;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Workspace.TemplateEngine;

/// <summary>
/// Provides snapshot-based rollback for template post-actions.
/// Before modifying a file, call <see cref="TrackFile"/> to snapshot its current content.
/// On failure, call <see cref="Rollback"/> to restore all tracked files.
/// On success, call <see cref="Commit"/> to discard snapshots.
/// </summary>
public sealed class PostActionTransaction : IDisposable
{
    private static readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(PostActionTransaction));

    // path → original content (null = file didn't exist before)
    private readonly Dictionary<string, byte[]?> _snapshots = new(StringComparer.OrdinalIgnoreCase);

    // Tracks directories created during post-actions (for cleanup on rollback)
    private readonly List<string> _createdDirectories = new();

    private bool _committed;

    /// <summary>
    /// Snapshots a file's current content before it gets modified.
    /// Call this BEFORE any post-action that might modify the file.
    /// Safe to call multiple times for the same path — only the first snapshot is kept.
    /// </summary>
    public void TrackFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (_snapshots.ContainsKey(fullPath)) return;

        _snapshots[fullPath] = File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : null;
    }

    /// <summary>
    /// Tracks a directory that was created during post-actions.
    /// On rollback, these directories are deleted if they were newly created.
    /// </summary>
    public void TrackNewDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path);
        _createdDirectories.Add(fullPath);
    }

    /// <summary>
    /// Restores all tracked files to their pre-post-action state.
    /// Files that didn't exist are deleted. Files that existed are restored.
    /// Newly created directories are removed.
    /// </summary>
    public void Rollback()
    {
        if (_committed) return;

        int restored = 0;
        int deleted = 0;
        int errors = 0;

        foreach (var (path, originalContent) in _snapshots)
        {
            try
            {
                if (originalContent == null)
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        deleted++;
                    }
                }
                else
                {
                    File.WriteAllBytes(path, originalContent);
                    restored++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to rollback '{Path}': {Error}", path, ex.Message);
                errors++;
            }
        }

        // Remove newly created directories (in reverse order to handle nesting)
        foreach (var dir in _createdDirectories.AsEnumerable().Reverse())
        {
            try
            {
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to remove directory '{Dir}': {Error}", dir, ex.Message);
            }
        }

        _logger.LogInformation("Rollback complete: {Restored} restored, {Deleted} deleted, {Errors} errors", restored, deleted, errors);
    }

    /// <summary>
    /// Marks the transaction as successful. Discards all snapshots.
    /// After commit, <see cref="Rollback"/> is a no-op.
    /// </summary>
    public void Commit()
    {
        _snapshots.Clear();
        _createdDirectories.Clear();
        _committed = true;
    }

    public void Dispose()
    {
        // Don't auto-rollback on dispose — the caller decides via Commit/Rollback
        _snapshots.Clear();
        _createdDirectories.Clear();
    }
}
