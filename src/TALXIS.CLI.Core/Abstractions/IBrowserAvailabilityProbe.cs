namespace TALXIS.CLI.Core.Abstractions;

/// <summary>
/// Reports whether the current environment can launch a local browser that
/// receives OAuth <c>http://localhost</c> redirects. Environments like
/// browser-based GitHub Codespaces, SSH sessions, and headless Linux containers
/// are interactive (TTY present) but "browser-isolated" — the user must use
/// device code flow instead of interactive browser login.
/// </summary>
/// <remarks>
/// This is distinct from <see cref="IHeadlessDetector"/> which determines
/// whether a human is present at all. A process can be non-headless (TTY
/// available, human present) yet still browser-isolated.
/// </remarks>
public interface IBrowserAvailabilityProbe
{
    /// <summary>
    /// <c>true</c> when the environment can open a browser that successfully
    /// receives the <c>http://localhost</c> redirect after Entra sign-in.
    /// </summary>
    bool IsBrowserAvailable { get; }

    /// <summary>Human-readable reason when <see cref="IsBrowserAvailable"/> is false.</summary>
    string? UnavailableReason { get; }
}
