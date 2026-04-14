using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace TALXIS.CLI.IntegrationTests;

/// <summary>
/// Result of a CLI command execution, including output and exit code.
/// </summary>
public record CliResult(int ExitCode, string Output, string Error);

/// <summary>
/// Executes CLI commands for testing purposes.
/// </summary>
public static class CliRunner
{
    private static readonly string CliProject = GetCliProjectPath();

    /// <summary>
    /// Runs a CLI command, splitting the command string by spaces.
    /// Use the <see cref="string[]"/> overload when arguments may contain spaces (e.g. file paths).
    /// Throws <see cref="InvalidOperationException"/> on non-zero exit code.
    /// </summary>
    public static Task<string> RunAsync(string command, string? workingDirectory = null)
        => RunAsync(command.Split(' ', StringSplitOptions.RemoveEmptyEntries), workingDirectory);

    /// <summary>
    /// Runs a CLI command with explicit argument tokens.
    /// Throws <see cref="InvalidOperationException"/> on non-zero exit code.
    /// </summary>
    public static async Task<string> RunAsync(string[] args, string? workingDirectory = null)
    {
        var result = await RunRawAsync(args, workingDirectory);

        if (result.ExitCode != 0)
        {
            var errorMessage = $"CLI command failed: {string.Join(' ', args)}\nExit code: {result.ExitCode}";
            if (!string.IsNullOrEmpty(result.Error))
                errorMessage += $"\nError: {result.Error}";
            if (!string.IsNullOrEmpty(result.Output))
                errorMessage += $"\nOutput: {result.Output}";

            throw new InvalidOperationException(errorMessage);
        }

        return result.Output;
    }

    /// <summary>
    /// Runs a CLI command and returns the full result without throwing on failure.
    /// Useful for asserting on expected error cases.
    /// </summary>
    public static Task<CliResult> RunRawAsync(string command, string? workingDirectory = null)
        => RunRawAsync(command.Split(' ', StringSplitOptions.RemoveEmptyEntries), workingDirectory);

    /// <summary>
    /// Runs a CLI command with explicit argument tokens and returns the full result without throwing on failure.
    /// </summary>
    public static async Task<CliResult> RunRawAsync(string[] args, string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory()
        };

        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(CliProject);
        psi.ArgumentList.Add("--no-build");
        psi.ArgumentList.Add("--");

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;

        // Read stdout and stderr concurrently to avoid deadlocks when
        // the child process fills one of the OS pipe buffers (~4 KB).
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        string output = await outputTask;
        string error = await errorTask;

        return new CliResult(process.ExitCode, output, error);
    }

    private static string GetCliProjectPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(baseDir);

        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TALXIS.CLI.sln")))
            dir = dir.Parent;

        if (dir == null)
            throw new InvalidOperationException("Could not find repository root");

        return Path.Combine(dir.FullName, "src", "TALXIS.CLI", "TALXIS.CLI.csproj");
    }
}
