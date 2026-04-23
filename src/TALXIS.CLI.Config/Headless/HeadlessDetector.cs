using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.Internal;
using TALXIS.CLI.Config.Resolution;

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

    public HeadlessDetector() : this(new ConsoleRedirectionProbe(), ProcessEnvironmentReader.Instance) { }

    internal HeadlessDetector(IConsoleRedirectionProbe probe, IEnvironmentReader env)
    {
        var reasons = new List<string>();

        if (EnvBool.IsTruthy(env.Get(TxcNonInteractive)))
            reasons.Add($"{TxcNonInteractive}=1");

        foreach (var ci in CiVariables)
        {
            if (EnvBool.IsTruthy(env.Get(ci)))
                reasons.Add($"{ci}={env.Get(ci)}");
        }

        if (probe.IsInputRedirected && probe.IsOutputRedirected)
            reasons.Add("stdin and stdout are redirected");

        IsHeadless = reasons.Count > 0;
        Reason = IsHeadless ? string.Join("; ", reasons) : null;
    }

    public bool IsHeadless { get; }
    public string? Reason { get; }
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
