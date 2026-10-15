using System.ComponentModel;
using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Flow;

/// <summary>
/// <c>txc environment flow validate</c> — checks a locally authored flow
/// definition against the connectors that actually exist.
/// </summary>
/// <remarks>
/// Flow definitions are expanded by hand, which makes invented operation IDs,
/// parameter names and action types the most common defect in a workspace.
/// This command is the gate that catches them before deployment.
/// </remarks>
[CliReadOnly]
[CliWorkflow("environment-inspection")]
[CliCommand(
    Name = "validate",
    Description = "Validates a locally authored cloud flow definition against connector metadata from the LIVE connected environment. Requires an active profile. Catches invented operation IDs, wrong parameter names, bad enum values and incorrect action types before deployment."
)]
public class FlowValidateCliCommand : ProfiledCliCommand
{
    /// <summary>File names a scaffolded flow component is likely to use.</summary>
    private static readonly string[] CandidateFileNames =
    [
        "clientdata.json",
        "definition.json",
        "workflow.json",
        "flow.json",
    ];

    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(FlowValidateCliCommand));

    [CliArgument(Description = "Path to the flow definition JSON file, or to the workflow component directory containing it.")]
    public string Path { get; set; } = ".";

    [CliOption(Name = "--connection-check", Required = false, Description = "Also verify that each connection reference exists in the environment.")]
    [DefaultValue(false)]
    public bool ConnectionCheck { get; set; }

    [CliOption(Name = "--offline", Required = false, Description = "Run structural rules only, without calling the environment.")]
    [DefaultValue(false)]
    public bool Offline { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var file = ResolveDefinitionFile(Path);
        if (file is null)
        {
            Logger.LogError(
                "No flow definition JSON found at '{Path}'. Point at the definition file, or at a directory containing one.",
                Path);
            return ExitValidationError;
        }

        var parsed = ReadJson(file);
        if (parsed.Error is not null)
        {
            Logger.LogError("Could not read '{File}': {Error}", file, parsed.Error);
            return ExitValidationError;
        }

        using var document = parsed.Document!;

        var service = TxcServices.Get<IPowerAutomateConnectorService>();
        var report = await service.ValidateDefinitionAsync(
            Profile, document.RootElement, ConnectionCheck, Offline, CancellationToken.None)
            .ConfigureAwait(false);

        OutputFormatter.WriteData(report, PrintReport);

        // Warnings alone still pass; only errors block.
        return report.Valid ? ExitSuccess : ExitValidationError;
    }

    /// <summary>
    /// Resolves the argument to a definition file. A directory is searched for
    /// the conventional names first, then for any single JSON file.
    /// </summary>
    private static string? ResolveDefinitionFile(string path)
    {
        var full = System.IO.Path.GetFullPath(path);

        if (File.Exists(full))
            return full;

        if (!Directory.Exists(full))
            return null;

        foreach (var candidate in CandidateFileNames)
        {
            var match = Directory
                .EnumerateFiles(full, candidate, SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            if (match is not null)
                return match;
        }

        var jsonFiles = Directory.GetFiles(full, "*.json", SearchOption.TopDirectoryOnly);
        return jsonFiles.Length == 1 ? jsonFiles[0] : null;
    }

    /// <summary>
    /// Reads and parses the file. Kept out of <c>ExecuteAsync</c> so a malformed
    /// file becomes a validation error with a useful message rather than an
    /// unhandled exception.
    /// </summary>
    private static (JsonDocument? Document, string? Error) ReadJson(string file)
    {
        try
        {
            return (JsonDocument.Parse(File.ReadAllText(file)), null);
        }
        catch (JsonException ex)
        {
            return (null, $"the file is not valid JSON ({ex.Message})");
        }
        catch (IOException ex)
        {
            return (null, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return (null, ex.Message);
        }
    }

    // Text-renderer callback invoked by OutputFormatter.WriteData — OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintReport(FlowValidationReport report)
    {
        if (report.Findings.Count == 0)
        {
            OutputWriter.WriteLine("Flow definition is valid.");
            return;
        }

        foreach (var finding in report.Findings
            .OrderByDescending(f => f.Severity == FlowValidationSeverity.Error)
            .ThenBy(f => f.Path, StringComparer.Ordinal))
        {
            OutputWriter.WriteLine($"{finding.Severity.ToUpperInvariant()} {finding.Path}");
            OutputWriter.WriteLine($"  [{finding.Code}] {finding.Message}");
        }

        OutputWriter.WriteLine(string.Empty);
        OutputWriter.WriteLine($"{report.ErrorCount} error(s), {report.WarningCount} warning(s).");
    }
#pragma warning restore TXC003
}
