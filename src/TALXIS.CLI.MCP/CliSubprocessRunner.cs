using System.Diagnostics;
using System.Reflection;

namespace TALXIS.CLI.MCP;

internal static class CliSubprocessRunner
{
    public static async Task<CliSubprocessResult> RunAsync(
        IReadOnlyList<string> cliArgs,
        ISubprocessOutputHandler? outputHandler,
        CancellationToken cancellationToken,
        string? workingDirectory = null)
    {
        using Process process = StartProcess(cliArgs, workingDirectory);

        if (outputHandler != null)
        {
            return await RunStreamingAsync(process, outputHandler, cancellationToken);
        }

        return await RunBufferedAsync(process, cancellationToken);
    }

    private static async Task<CliSubprocessResult> RunStreamingAsync(
        Process process,
        ISubprocessOutputHandler handler,
        CancellationToken cancellationToken)
    {
        // Read stdout and stderr concurrently, line-by-line
        Task stdoutTask = ReadLinesAsync(process.StandardOutput, handler.OnStdoutLineAsync, cancellationToken);
        Task stderrTask = ReadLinesAsync(process.StandardError, handler.OnStderrLineAsync, cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            try { await process.WaitForExitAsync(); } catch (InvalidOperationException) { }
            // Drain stream reader tasks to avoid unobserved exceptions
            try { await Task.WhenAll(stdoutTask, stderrTask); }
            catch (Exception) when (true) { /* swallow IO/cancellation from killed process */ }
            throw;
        }

        // Drain any remaining output after process exits
        await Task.WhenAll(stdoutTask, stderrTask);

        await handler.OnProcessExitedAsync(process.ExitCode);

        return new CliSubprocessResult(process.ExitCode, handler);
    }

    private static async Task<CliSubprocessResult> RunBufferedAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            try { await process.WaitForExitAsync(); } catch (InvalidOperationException) { }
            // Drain stream reader tasks to avoid unobserved exceptions
            try { await Task.WhenAll(stdoutTask, stderrTask); }
            catch (Exception) when (true) { /* swallow IO/cancellation from killed process */ }
            throw;
        }

        string stdout = await stdoutTask;
        string stderr = await stderrTask;

        return new CliSubprocessResult(process.ExitCode, CombineOutput(stdout, stderr));
    }

    private static async Task ReadLinesAsync(
        StreamReader reader,
        Func<string, Task> lineHandler,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line == null)
                break; // EOF

            await lineHandler(line);
        }
    }

    private static Process StartProcess(IReadOnlyList<string> cliArgs, string? workingDirectory = null)
    {
        (string fileName, string? assemblyPath) = ResolveCliHost();
        ProcessStartInfo startInfo = new()
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
                ? workingDirectory
                : System.Environment.CurrentDirectory
        };

        // Enable structured JSON logging for MCP consumption
        startInfo.Environment["TXC_LOG_FORMAT"] = "json";

        // Force headless mode for every MCP-spawned tool invocation so that
        // interactive auth flows (browser, device code, masked secret prompts)
        // can never run: stdout is reserved for JSON-RPC frames and the
        // process has stdin/stdout redirected. Leaf commands must fail fast
        // with a structured AUTH_REQUIRED-style error instead of hanging on
        // Console.ReadKey. See src/TALXIS.CLI.MCP/README.md#auth-contract.
        startInfo.Environment["TXC_NON_INTERACTIVE"] = "1";

        startInfo.FileName = fileName;
        if (!string.IsNullOrWhiteSpace(assemblyPath))
        {
            startInfo.ArgumentList.Add(assemblyPath);
        }

        foreach (string cliArg in cliArgs)
        {
            startInfo.ArgumentList.Add(cliArg);
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the txc CLI subprocess.");
    }

    private static (string FileName, string? AssemblyPath) ResolveCliHost()
    {
        string cliAssemblyPath = typeof(TALXIS.CLI.Program).Assembly.Location;
        if (string.IsNullOrWhiteSpace(cliAssemblyPath))
        {
            throw new InvalidOperationException("Could not resolve the TALXIS CLI assembly path.");
        }

        string cliDirectory = Path.GetDirectoryName(cliAssemblyPath)
            ?? throw new InvalidOperationException("Could not resolve the TALXIS CLI directory.");

        string cliExecutableName = OperatingSystem.IsWindows() ? "TALXIS.CLI.exe" : "TALXIS.CLI";
        string cliExecutablePath = Path.Combine(cliDirectory, cliExecutableName);
        if (File.Exists(cliExecutablePath))
        {
            return (cliExecutablePath, null);
        }

        return ("dotnet", cliAssemblyPath);
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private static string CombineOutput(string stdout, string stderr)
    {
        if (string.IsNullOrEmpty(stdout))
        {
            return stderr;
        }

        if (string.IsNullOrEmpty(stderr))
        {
            return stdout;
        }

        return stdout.TrimEnd() + System.Environment.NewLine + stderr;
    }
}

internal sealed class CliSubprocessResult
{
    public int ExitCode { get; }
    public string Output { get; }

    /// <summary>Creates a result from buffered output.</summary>
    public CliSubprocessResult(int exitCode, string output)
    {
        ExitCode = exitCode;
        Output = output;
    }

    /// <summary>Creates a result from a streaming output handler (uses stdout content as output).</summary>
    public CliSubprocessResult(int exitCode, ISubprocessOutputHandler handler)
    {
        ExitCode = exitCode;
        if (handler is McpLogForwarder forwarder)
        {
            Output = forwarder.StdoutContent;
            LastErrors = forwarder.LastErrors;
            FullLog = forwarder.FullLog;
        }
        else
        {
            Output = string.Empty;
            LastErrors = string.Empty;
            FullLog = string.Empty;
        }
    }

    /// <summary>Error messages captured from subprocess stderr logs.</summary>
    public string LastErrors { get; } = string.Empty;

    /// <summary>Complete stderr log from the subprocess (all levels).</summary>
    public string FullLog { get; } = string.Empty;
}
