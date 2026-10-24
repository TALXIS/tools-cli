using System.Text;
using Microsoft.Extensions.Logging;
using TALXIS.Platform.Metadata.Packaging;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

/// <summary>
/// Wraps the SolutionPackager so its progress text ("Extracting …", "Processing Component: …")
/// goes to the log instead of stdout. SolutionPackagerLib writes straight to
/// <see cref="Console.Out"/>, which put that text in front of the command's result and broke
/// <c>--format json</c> for export, unpack, pack, pull and clone (#217).
/// </summary>
/// <remarks>
/// The log, not raw stderr: the MCP server reads the CLI's stderr as structured log lines
/// and reports any other stderr text as an error. Swapping <see cref="Console.Out"/> is
/// process-wide, which is fine here because the CLI runs one command per process (MCP runs
/// CLI commands as subprocesses).
/// </remarks>
internal sealed class LoggedSolutionPackagerService : ISolutionPackagerService
{
    private readonly ISolutionPackagerService _inner;
    private readonly ILogger _logger;

    public LoggedSolutionPackagerService(ISolutionPackagerService inner, ILogger logger)
    {
        _inner = inner;
        _logger = logger;
    }

    public SolutionPackagerResult Pack(string folder, string zipPath, bool managed)
        => WithConsoleOutLogged(() => _inner.Pack(folder, zipPath, managed));

    public SolutionPackagerResult Pack(string folder, string zipPath, SolutionPackagerOptions options)
        => WithConsoleOutLogged(() => _inner.Pack(folder, zipPath, options));

    public SolutionPackagerResult Unpack(string zipPath, string outputFolder, bool managed)
        => WithConsoleOutLogged(() => _inner.Unpack(zipPath, outputFolder, managed));

    public SolutionPackagerResult Unpack(string zipPath, string outputFolder, SolutionPackagerOptions options)
        => WithConsoleOutLogged(() => _inner.Unpack(zipPath, outputFolder, options));

    private SolutionPackagerResult WithConsoleOutLogged(Func<SolutionPackagerResult> run)
    {
#pragma warning disable RS0030 // Redirecting the packager's own Console.Out writes, not writing command output
        var original = Console.Out;
        using var lines = new LogLineWriter(_logger);
        Console.SetOut(lines);
        try
        {
            return run();
        }
        finally
        {
            lines.Flush();
            Console.SetOut(original);
        }
#pragma warning restore RS0030
    }

    /// <summary>
    /// Turns written text into one log entry per non-blank line, keeping the severity of the
    /// packager's "Error: …" and "Warning: …" lines.
    /// </summary>
    private sealed class LogLineWriter : TextWriter
    {
        private readonly ILogger _logger;
        private readonly StringBuilder _line = new();

        public LogLineWriter(ILogger logger) => _logger = logger;

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            if (value == '\n')
                EmitLine();
            else if (value != '\r')
                _line.Append(value);
        }

        public override void Flush() => EmitLine();

        private void EmitLine()
        {
            var text = _line.ToString().Trim();
            _line.Clear();
            if (text.Length > 0)
                _logger.Log(LevelOf(text), "{PackagerOutput}", text);
        }

        private static LogLevel LevelOf(string line)
        {
            if (StartsWithWord(line, "error"))
                return LogLevel.Error;
            if (StartsWithWord(line, "warning") || StartsWithWord(line, "warn"))
                return LogLevel.Warning;
            return LogLevel.Information;
        }

        // "Error: …" or "Error …", but not a word that merely starts with it ("Errors found 0").
        private static bool StartsWithWord(string line, string word)
            => line.StartsWith(word, StringComparison.OrdinalIgnoreCase)
               && (line.Length == word.Length || line[word.Length] is ':' or ' ');
    }
}
