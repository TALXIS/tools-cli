using TALXIS.CLI.Config.Abstractions;

namespace TALXIS.CLI.Config.Headless;

/// <summary>
/// Determines whether the current process is running without a usable TTY.
/// Interactive and device-code authentication flows are forbidden when
/// <see cref="IsHeadless"/> is true.
/// </summary>
public sealed class HeadlessDetector : IHeadlessDetector
{
    public const string TxcNonInteractive = "TXC_NON_INTERACTIVE";

    private static readonly string[] CiVariables =
    {
        "CI", "GITHUB_ACTIONS", "TF_BUILD",
    };

    public HeadlessDetector() : this(new ConsoleRedirectionProbe(), ProcessEnv) { }

    internal HeadlessDetector(IConsoleRedirectionProbe probe, Func<string, string?> getEnv)
    {
        var reasons = new List<string>();

        if (IsTruthy(getEnv(TxcNonInteractive)))
            reasons.Add($"{TxcNonInteractive}=1");

        foreach (var ci in CiVariables)
        {
            if (IsTruthy(getEnv(ci)))
                reasons.Add($"{ci}={getEnv(ci)}");
        }

        if (probe.IsInputRedirected && probe.IsOutputRedirected)
            reasons.Add("stdin and stdout are redirected");

        IsHeadless = reasons.Count > 0;
        Reason = IsHeadless ? string.Join("; ", reasons) : null;
    }

    public bool IsHeadless { get; }
    public string? Reason { get; }

    private static string? ProcessEnv(string name) => Environment.GetEnvironmentVariable(name);

    private static bool IsTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.Equals("1", StringComparison.Ordinal)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}

internal interface IConsoleRedirectionProbe
{
    bool IsInputRedirected { get; }
    bool IsOutputRedirected { get; }
}

internal sealed class ConsoleRedirectionProbe : IConsoleRedirectionProbe
{
    public bool IsInputRedirected => Console.IsInputRedirected;
    public bool IsOutputRedirected => Console.IsOutputRedirected;
}
