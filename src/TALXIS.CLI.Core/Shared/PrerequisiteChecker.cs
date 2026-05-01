using System.Diagnostics;

namespace TALXIS.CLI.Core.Shared;

/// <summary>
/// Validates that required external tools are available.
/// Returns a list of problems — callers decide how to report them.
/// </summary>
public static class PrerequisiteChecker
{
    /// <summary>
    /// Checks that pwsh is available. Returns an empty list if all prerequisites are met.
    /// .NET SDK version is not checked — if the CLI is running, the SDK is already sufficient.
    /// </summary>
    public static List<string> CheckScaffoldingPrerequisites()
    {
        var problems = new List<string>();

        if (!IsCommandAvailable("pwsh", "--version"))
            problems.Add("PowerShell (pwsh) is not installed. Template post-action scripts require it. Install from: https://learn.microsoft.com/powershell/scripting/install/installing-powershell");

        return problems;
    }

    private static bool IsCommandAvailable(string command, string args)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo(command, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            proc?.WaitForExit(5000);
            return proc is not null && proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
