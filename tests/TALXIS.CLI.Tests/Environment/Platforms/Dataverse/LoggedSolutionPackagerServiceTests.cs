using System.Reflection;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Platform.Dataverse.Application.Services;
using TALXIS.Platform.Metadata.Packaging;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Platforms.Dataverse;

/// <summary>
/// SolutionPackagerLib writes progress to Console.Out; the wrapper must move it to the log
/// so stdout stays the command's JSON result (#217).
/// </summary>
/// <remarks>
/// Console.Out is process-wide and other tests run in parallel, so these assert on what the
/// packager saw and on log entries being present, never on Console.Out staying empty.
/// </remarks>
public sealed class LoggedSolutionPackagerServiceTests
{
    [Fact]
    public void Unpack_PackagerConsoleText_GoesToLogNotStdout()
    {
        var log = new List<(LogLevel Level, string Message)>();
        var original = Console.Out;
        var inner = new ChattyPackager();

        var result = new LoggedSolutionPackagerService(inner, new CaptureLogger(log))
            .Unpack("solution.zip", "out", managed: false);

        Assert.Same(ChattyPackager.Result, result);
        Assert.NotSame(original, inner.SeenConsoleOut);
        Assert.Contains((LogLevel.Information, "Extracting solution.zip to out"), log);
        Assert.Contains((LogLevel.Information, "Processing Component: Roles"), log);
        Assert.Contains((LogLevel.Information, "- Catalog Viewer"), log);
        Assert.Contains((LogLevel.Information, "Unmanaged Extract complete."), log);
        Assert.DoesNotContain(log, e => string.IsNullOrWhiteSpace(e.Message));
        Assert.Same(original, Console.Out);
    }

    [Fact]
    public void Pack_PackagerThrows_RestoresConsoleOutAndKeepsPartialLine()
    {
        var log = new List<(LogLevel Level, string Message)>();
        var original = Console.Out;
        var inner = new ChattyPackager { ThrowAfterPartialLine = true };

        Assert.Throws<InvalidOperationException>(() =>
            new LoggedSolutionPackagerService(inner, new CaptureLogger(log)).Pack("folder", "solution.zip", managed: true));

        Assert.Same(original, Console.Out);
        Assert.Contains((LogLevel.Information, "Packing folder"), log);
    }

    [Fact]
    public void Unpack_PackagerErrorAndWarningLines_KeepTheirSeverity()
    {
        var log = new List<(LogLevel Level, string Message)>();
        var inner = new ChattyPackager
        {
            ExtraLines = new[] { "Error: Root component missing", "Warning: Skipping localization", "Errors found: 0" },
        };

        new LoggedSolutionPackagerService(inner, new CaptureLogger(log)).Unpack("solution.zip", "out", managed: false);

        Assert.Contains((LogLevel.Error, "Error: Root component missing"), log);
        Assert.Contains((LogLevel.Warning, "Warning: Skipping localization"), log);
        Assert.Contains((LogLevel.Information, "Errors found: 0"), log);
        Assert.Contains((LogLevel.Information, "Processing Component: Roles"), log);
    }

    private sealed class ChattyPackager : ISolutionPackagerService
    {
        // The result's constructor is internal to the packaging library.
        public static readonly SolutionPackagerResult Result = (SolutionPackagerResult)Activator.CreateInstance(
            typeof(SolutionPackagerResult), BindingFlags.NonPublic | BindingFlags.Instance, binder: null,
            args: new object[] { Array.Empty<string>(), Array.Empty<string>() }, culture: null)!;

        public TextWriter? SeenConsoleOut { get; private set; }
        public bool ThrowAfterPartialLine { get; init; }
        public string[] ExtraLines { get; init; } = Array.Empty<string>();

        public SolutionPackagerResult Pack(string folder, string zipPath, bool managed)
        {
            SeenConsoleOut = Console.Out;
            Console.Out.Write($"\r\nPacking {folder}");
            if (ThrowAfterPartialLine)
                throw new InvalidOperationException("packager failed");
            return Result;
        }

        public SolutionPackagerResult Pack(string folder, string zipPath, SolutionPackagerOptions options)
            => Pack(folder, zipPath, managed: false);

        public SolutionPackagerResult Unpack(string zipPath, string outputFolder, bool managed)
        {
            SeenConsoleOut = Console.Out;
            Console.Out.WriteLine();
            Console.Out.WriteLine($"Extracting {zipPath} to {outputFolder}");
            Console.Out.WriteLine();
            Console.Out.WriteLine("Processing Component: Roles");
            Console.Out.WriteLine(" - Catalog Viewer");
            foreach (var line in ExtraLines)
                Console.Out.WriteLine(line);
            Console.Out.Write("\r\nUnmanaged Extract complete.\r\n");
            return Result;
        }

        public SolutionPackagerResult Unpack(string zipPath, string outputFolder, SolutionPackagerOptions options)
            => Unpack(zipPath, outputFolder, managed: false);
    }
}

/// <summary>Minimal <see cref="ILogger"/> that records log entries for assertions.</summary>
file sealed class CaptureLogger(List<(LogLevel Level, string Message)> sink) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (sink)
            sink.Add((logLevel, formatter(state, exception)));
    }
}
