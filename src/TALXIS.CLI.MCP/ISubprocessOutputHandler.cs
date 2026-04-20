namespace TALXIS.CLI.MCP;

/// <summary>
/// Callback interface for processing subprocess output line-by-line.
/// </summary>
internal interface ISubprocessOutputHandler
{
    /// <summary>Called for each line read from the subprocess stdout.</summary>
    Task OnStdoutLineAsync(string line);

    /// <summary>Called for each line read from the subprocess stderr (JSON log lines in MCP mode).</summary>
    Task OnStderrLineAsync(string line);

    /// <summary>Called when the subprocess has exited.</summary>
    Task OnProcessExitedAsync(int exitCode);
}
