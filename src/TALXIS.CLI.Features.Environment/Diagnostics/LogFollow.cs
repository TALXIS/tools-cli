namespace TALXIS.CLI.Features.Environment.Diagnostics;

/// <summary>
/// Helpers for the <c>--follow</c> live-tail loop.
/// </summary>
internal static class FollowSupport
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Parses <c>--interval</c> (whole seconds). Empty/null → default 5s.
    /// Rejects non-integer input and intervals below the 2s floor.
    /// </summary>
    public static bool TryParseInterval(string? value, out TimeSpan interval, out string? error)
    {
        interval = DefaultInterval;
        error = null;

        if (string.IsNullOrWhiteSpace(value))
            return true;

        if (!int.TryParse(value.Trim(), out var seconds))
        {
            error = $"Invalid --interval value '{value}'. Expected whole seconds (e.g. 5).";
            return false;
        }

        if (seconds < MinInterval.TotalSeconds)
        {
            error = $"--interval must be at least {(int)MinInterval.TotalSeconds} seconds.";
            return false;
        }

        interval = TimeSpan.FromSeconds(seconds);
        return true;
    }
}

/// <summary>
/// Stateful dedup for a live tail: tracks which rows have already been printed
/// (by a string key) and returns only the unseen ones, in chronological
/// (oldest-first) order suitable for streaming.
/// </summary>
internal sealed class FollowTracker<T>
{
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly Func<T, string?> _key;

    public FollowTracker(Func<T, string?> key) => _key = key ?? throw new ArgumentNullException(nameof(key));

    /// <summary>
    /// Given a freshly fetched batch (newest-first, as the readers return it),
    /// returns the rows not seen before, ordered oldest-first, and marks them seen.
    /// </summary>
    public IReadOnlyList<T> SelectNew(IEnumerable<T> fetched)
    {
        var fresh = new List<T>();
        foreach (var item in fetched)
        {
            var key = _key(item);
            if (!string.IsNullOrEmpty(key) && _seen.Add(key))
                fresh.Add(item);
        }
        // Input is newest-first; reverse so the tail prints oldest-first.
        fresh.Reverse();
        return fresh;
    }
}
