using TALXIS.CLI.Config.Headless;
using TALXIS.CLI.Config.Model;

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

/// <summary>
/// Standard fail-fast extensions for <see cref="IHeadlessDetector"/>.
/// </summary>
public static class HeadlessDetectorExtensions
{
    /// <summary>
    /// Throws <see cref="HeadlessAuthRequiredException"/> if the process is
    /// headless and <paramref name="kind"/> is not in
    /// <see cref="HeadlessAuthRequiredException.PermittedHeadlessKinds"/>.
    /// </summary>
    public static void EnsureKindAllowed(this IHeadlessDetector detector, CredentialKind kind)
    {
        ArgumentNullException.ThrowIfNull(detector);
        if (!detector.IsHeadless) return;
        if (HeadlessAuthRequiredException.PermittedHeadlessKinds.Contains(kind)) return;
        throw new HeadlessAuthRequiredException(kind, detector.Reason ?? "non-interactive environment");
    }
}
