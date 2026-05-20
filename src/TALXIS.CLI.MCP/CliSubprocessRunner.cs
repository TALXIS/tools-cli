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

        return handler is McpLogForwarder forwarder
            ? new CliSubprocessResult(process.ExitCode, forwarder)
            : new CliSubprocessResult(process.ExitCode, string.Empty);
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
            WorkingDirectory = workingDirectory ?? System.Environment.CurrentDirectory
        };

        // Enable structured JSON logging for MCP consumption
        startInfo.Environment["TXC_LOG_FORMAT"] = "json";

        // Propagate trace context so the subprocess's Activity span becomes a child
        // of the MCP server's current span. Both export independently to App Insights,
        // but they appear as a single correlated trace (MCP dispatch → CLI execution).
        var currentActivity = System.Diagnostics.Activity.Current;
        if (currentActivity != null)
        {
            // W3C traceparent format: 00-{traceId}-{spanId}-{flags}
            var flags = (currentActivity.ActivityTraceFlags & System.Diagnostics.ActivityTraceFlags.Recorded) != 0 ? "01" : "00";
            startInfo.Environment["TXC_TRACEPARENT"] = $"00-{currentActivity.TraceId}-{currentActivity.SpanId}-{flags}";
        }

        // Propagate the resolved session ID so CLI subprocesses use the same
        // session identifier as the MCP server. The child's SessionIdResolver
        // picks this up via ExplicitEnvVarStrategy (highest priority).
        // Also propagate the original source so the child doesn't report "explicit"
        // when the real source was e.g. "copilot".
        var sessionResolver = TALXIS.CLI.Logging.TxcTelemetrySetup.SessionResolver;
        if (sessionResolver != null)
        {
            startInfo.Environment[TALXIS.CLI.Logging.SessionId.ExplicitEnvVarStrategy.EnvVar] = sessionResolver.SessionId;
            startInfo.Environment[TALXIS.CLI.Logging.SessionId.ExplicitEnvVarStrategy.SourceEnvVar] = sessionResolver.Source;
        }

        // Tell the child CLI process it was invoked from MCP, not standalone terminal.
        // This ensures txc.entry_point=mcp on all child spans.
        startInfo.Environment["TXC_ENTRY_POINT"] = "mcp";

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

    /// <summary>Creates a result with explicit stdout/stderr-derived diagnostic fields.</summary>
    internal CliSubprocessResult(int exitCode, string output, string lastErrors,
        IReadOnlyList<RedactedLogEntry>? structuredEntries = null)
    {
        ExitCode = exitCode;
        Output = output;
        LastErrors = lastErrors;
        StructuredEntries = structuredEntries ?? [];
    }

    /// <summary>Creates a result from an MCP log forwarder that captured subprocess output.</summary>
    public CliSubprocessResult(int exitCode, McpLogForwarder forwarder)
    {
        ExitCode = exitCode;
        Output = forwarder.StdoutContent;
        LastErrors = forwarder.LastErrors;
        StructuredEntries = forwarder.StructuredEntries;
    }

    /// <summary>Error messages captured from subprocess stderr logs.</summary>
    public string LastErrors { get; } = string.Empty;

    /// <summary>Structured log entries captured from subprocess stderr (preserves level, category, data for filtering).</summary>
    public IReadOnlyList<RedactedLogEntry> StructuredEntries { get; } = [];
}
