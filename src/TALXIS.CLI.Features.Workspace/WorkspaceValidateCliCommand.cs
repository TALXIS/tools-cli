using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;
using TALXIS.Platform.Metadata.Validation;

namespace TALXIS.CLI.Features.Workspace;

[CliReadOnly]
[CliCommand(
    Name = "validate",
    Description = "Validates solution workspace files against XSD schemas, checks for structural issues, and loads the metadata model.")]
public sealed class WorkspaceValidateCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(WorkspaceValidateCliCommand));

    [CliArgument(Description = "Path to the solution project directory to validate.")]
    public string Path { get; set; } = ".";

    [CliOption(Name = "--file", Description = "Validate a single file (relative path within the workspace).")]
    public string? File { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var fullPath = System.IO.Path.GetFullPath(Path);
        if (!Directory.Exists(fullPath))
        {
            Logger.LogError("Directory not found: {Path}", fullPath);
            return ExitError;
        }

        IReadOnlyList<ValidationResult> results;

        if (File != null)
        {
            // Single file validation
            var filePath = System.IO.Path.Combine(fullPath, File);
            if (!System.IO.File.Exists(filePath))
            {
                Logger.LogError("File not found: {File}", filePath);
                return ExitError;
            }
            var schemaValidator = new SchemaValidator();
            results = schemaValidator.ValidateFile(filePath);
        }
        else
        {
            // Full workspace validation
            var validator = new WorkspaceValidator();
            var report = validator.ValidateDirectory(fullPath);
            results = report.Results;

            // Show component summary if model loaded
            if (report.LoadedComponents != null)
            {
                Logger.LogInformation("Components: {Summary}", report.LoadedComponents.ToString());
            }
        }

        int errors = 0;
        int warnings = 0;
        foreach (var result in results)
        {
            var file = result.FilePath != null
                ? System.IO.Path.GetRelativePath(fullPath, result.FilePath)
                : "unknown";
            var location = result.Line.HasValue ? $"({result.Line},{result.Column ?? 0})" : "";

            if (result.Severity == ValidationSeverity.Error)
            {
                Logger.LogError("{File}{Location}: {Message}", file, location, result.Message);
                errors++;
            }
            else
            {
                Logger.LogWarning("{File}{Location}: {Message}", file, location, result.Message);
                warnings++;
            }
        }

        if (errors == 0 && warnings == 0)
        {
            OutputFormatter.WriteResult("succeeded", $"Validation passed");
            return ExitSuccess;
        }

        OutputFormatter.WriteResult(errors > 0 ? "failed" : "succeeded",
            $"Validation complete: {errors} error(s), {warnings} warning(s)");
        return errors > 0 ? ExitError : ExitSuccess;
    }
}
