using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;
using TALXIS.Platform.Metadata.Validation;

namespace TALXIS.CLI.Features.Workspace;

[CliReadOnly]
[CliCommand(
    Name = "validate",
    Description = "Validates solution workspace files against XSD schemas and checks for structural issues.")]
public sealed class WorkspaceValidateCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(WorkspaceValidateCliCommand));

    [CliArgument(Description = "Path to the solution project directory to validate.")]
    public string Path { get; set; } = ".";

    protected override async Task<int> ExecuteAsync()
    {
        var fullPath = System.IO.Path.GetFullPath(Path);
        if (!Directory.Exists(fullPath))
        {
            Logger.LogError("Directory not found: {Path}", fullPath);
            return ExitError;
        }

        var schemaValidator = new SchemaValidator();
        var guidValidator = new GuidValidator();
        var allResults = new List<ValidationResult>();

        foreach (var xmlFile in Directory.EnumerateFiles(fullPath, "*.xml", SearchOption.AllDirectories))
        {
            var results = schemaValidator.ValidateFile(xmlFile);
            allResults.AddRange(results);
        }

        var guidResults = guidValidator.ValidateDirectory(fullPath);
        allResults.AddRange(guidResults);

        int errors = 0;
        int warnings = 0;
        foreach (var result in allResults)
        {
            if (result.Severity == ValidationSeverity.Error)
            {
                Logger.LogError("[{File}] {Message}", result.FilePath ?? "unknown", result.Message);
                errors++;
            }
            else
            {
                Logger.LogWarning("[{File}] {Message}", result.FilePath ?? "unknown", result.Message);
                warnings++;
            }
        }

        if (errors == 0 && warnings == 0)
        {
            OutputFormatter.WriteResult("succeeded", $"Validation passed — no issues found in {fullPath}");
            return ExitSuccess;
        }

        OutputFormatter.WriteResult(errors > 0 ? "failed" : "succeeded",
            $"Validation complete: {errors} error(s), {warnings} warning(s)");
        return errors > 0 ? ExitError : ExitSuccess;
    }
}
