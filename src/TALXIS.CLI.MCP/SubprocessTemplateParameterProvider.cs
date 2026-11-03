using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using TALXIS.CLI.Core;

namespace TALXIS.CLI.MCP;

/// <summary>
/// Production <see cref="ITemplateParameterProvider"/>: shells out to
/// `txc workspace component parameter list &lt;type&gt; --format json` (the same
/// CLI subprocess path the rest of the MCP server uses) and caches the result for
/// the lifetime of the process — template parameters don't change at runtime.
/// </summary>
internal sealed class SubprocessTemplateParameterProvider : ITemplateParameterProvider
{
    private readonly Func<CancellationToken, Task<string?>> _workingDirectoryProvider;
    private readonly ConcurrentDictionary<string, IReadOnlyList<TemplateParameterInfo>?> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public SubprocessTemplateParameterProvider(Func<CancellationToken, Task<string?>> workingDirectoryProvider)
    {
        _workingDirectoryProvider = workingDirectoryProvider;
    }

    public async Task<IReadOnlyList<TemplateParameterInfo>?> GetParametersAsync(string templateShortName, CancellationToken ct)
    {
        if (_cache.TryGetValue(templateShortName, out var cached))
            return cached;

        IReadOnlyList<TemplateParameterInfo>? parsed = null;
        try
        {
            var workingDirectory = await _workingDirectoryProvider(ct).ConfigureAwait(false);
            var args = new[] { "workspace", "component", "parameter", "list", templateShortName, "--format", "json" };
            var handler = new StdoutCaptureHandler();
            var result = await CliSubprocessRunner.RunAsync(args, handler, ct, workingDirectory).ConfigureAwait(false);

            if (result.ExitCode == 0)
                parsed = ParseParameters(handler.GetStdout());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Enrichment is best-effort — a lookup failure must never break the guide.
            parsed = null;
        }

        _cache[templateShortName] = parsed;
        return parsed;
    }

    /// <summary>
    /// Extracts the JSON array from captured stdout (tolerant of any leading/trailing
    /// noise) and deserializes it into parameter records.
    /// </summary>
    private static IReadOnlyList<TemplateParameterInfo>? ParseParameters(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
            return null;

        var start = stdout.IndexOf('[');
        var end = stdout.LastIndexOf(']');
        if (start < 0 || end <= start)
            return null;

        var json = stdout[start..(end + 1)];
        try
        {
            return JsonSerializer.Deserialize<List<TemplateParameterInfo>>(json, TxcOutputJsonOptions.Default);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Collects subprocess stdout lines; ignores stderr (JSON log lines in MCP mode).</summary>
    private sealed class StdoutCaptureHandler : ISubprocessOutputHandler
    {
        private readonly StringBuilder _stdout = new();

        public Task OnStdoutLineAsync(string line)
        {
            _stdout.AppendLine(line);
            return Task.CompletedTask;
        }

        public Task OnStderrLineAsync(string line) => Task.CompletedTask;

        public Task OnProcessExitedAsync(int exitCode) => Task.CompletedTask;

        public string GetStdout() => _stdout.ToString();
    }
}
