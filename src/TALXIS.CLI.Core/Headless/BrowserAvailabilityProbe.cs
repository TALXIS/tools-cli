using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Resolution;

namespace TALXIS.CLI.Core.Headless;

/// <summary>
/// Determines whether a local browser can open and receive
/// <c>http://localhost</c> OAuth redirects. Returns <c>false</c> in
/// browser-isolated environments such as:
/// <list type="bullet">
///   <item><c>TXC_TTY_ONLY=1</c> — explicit opt-in to device code flow.</item>
///   <item><c>CODESPACES=true</c> — browser-based GitHub Codespaces cannot tunnel localhost.</item>
///   <item>Linux without <c>DISPLAY</c> or <c>WAYLAND_DISPLAY</c> — no desktop session.</item>
///   <item>Linux where <c>xdg-open</c> is not on <c>PATH</c>.</item>
/// </list>
/// </summary>
public sealed class BrowserAvailabilityProbe : IBrowserAvailabilityProbe
{
    public const string TxcTtyOnlyEnvVar = "TXC_TTY_ONLY";

    public BrowserAvailabilityProbe() : this(ProcessEnvironmentReader.Instance) { }

    internal BrowserAvailabilityProbe(IEnvironmentReader env)
    {
        ArgumentNullException.ThrowIfNull(env);
        UnavailableReason = Evaluate(env);
        IsBrowserAvailable = UnavailableReason is null;
    }

    public bool IsBrowserAvailable { get; }
    public string? UnavailableReason { get; }

    private static string? Evaluate(IEnvironmentReader env)
    {
        // Explicit opt-in to TTY-only mode (device code).
        if (IsTruthy(env.Get(TxcTtyOnlyEnvVar)))
            return $"{TxcTtyOnlyEnvVar}=1";

        // GitHub Codespaces browser-based: localhost redirects cannot reach
        // the container from the user's browser tab.
        if (IsTruthy(env.Get("CODESPACES")))
            return "CODESPACES=true (localhost redirect unreachable)";

        // On non-Linux platforms, assume a browser is available (Windows/macOS
        // desktop or VS Code Desktop with tunnel — both handle localhost fine).
        if (!OperatingSystem.IsLinux())
            return null;

        // Linux: need a display server for the browser to render in.
        var display = env.Get("DISPLAY");
        var wayland = env.Get("WAYLAND_DISPLAY");
        if (string.IsNullOrWhiteSpace(display) && string.IsNullOrWhiteSpace(wayland))
            return "No DISPLAY or WAYLAND_DISPLAY set (no desktop session)";

        // Linux with a display: check xdg-open is on PATH.
        // Read PATH via the injected reader so tests can control it deterministically.
        if (!IsXdgOpenOnPath(env.Get("PATH")))
            return "xdg-open not found on PATH";

        return null;
    }

    private static bool IsXdgOpenOnPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var dirs = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in dirs)
        {
            var candidate = Path.Combine(dir, "xdg-open");
            if (File.Exists(candidate))
                return true;
        }
        return false;
    }

    private static bool IsTruthy(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && (value.Equals("1", StringComparison.Ordinal)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase));
}
