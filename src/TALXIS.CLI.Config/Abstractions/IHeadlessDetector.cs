namespace TALXIS.CLI.Config.Abstractions;

/// <summary>
/// Reports whether the current process is running in a non-interactive
/// (headless / CI) context. Used to forbid interactive and device-code
/// authentication flows outside TTY sessions.
/// </summary>
public interface IHeadlessDetector
{
    bool IsHeadless { get; }

    /// <summary>Human-readable reason for the last determination (e.g. "CI=true", "stdin redirected").</summary>
    string? Reason { get; }
}
