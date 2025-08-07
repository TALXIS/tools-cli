using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;
using Microsoft.TemplateEngine.Utils;
using CreationResultStatus = Microsoft.TemplateEngine.Edge.Template.CreationResultStatus;
using ITemplateCreationResult = Microsoft.TemplateEngine.Edge.Template.ITemplateCreationResult;

namespace TALXIS.CLI.Workspace.TemplateEngine
{
    /// <summary>
    /// Service responsible for creating templates from template definitions.
    /// </summary>
    public class TemplateCreationService
    {
        private readonly TemplateDiscoveryService _templateDiscoveryService;
        private readonly TemplateParameterValidator _parameterValidator;
        private readonly TemplateCreator _templateCreator;
        private readonly IEngineEnvironmentSettings _environmentSettings;

        public TemplateCreationService(
            TemplateDiscoveryService templateDiscoveryService,
            TemplateParameterValidator parameterValidator,
            TemplateCreator templateCreator,
            IEngineEnvironmentSettings environmentSettings)
        {
            _templateDiscoveryService = templateDiscoveryService ?? throw new ArgumentNullException(nameof(templateDiscoveryService));
            _parameterValidator = parameterValidator ?? throw new ArgumentNullException(nameof(parameterValidator));
            _templateCreator = templateCreator ?? throw new ArgumentNullException(nameof(templateCreator));
            _environmentSettings = environmentSettings ?? throw new ArgumentNullException(nameof(environmentSettings));
        }

        public async Task<TemplateScaffoldResult> ScaffoldAsync(
            string shortName, 
            string outputPath, 
            IDictionary<string, string> parameters, 
            string? version = null, 
            CancellationToken cancellationToken = default)
        {
            // Validate inputs
            ValidateInputs(shortName, outputPath);

            // Get template
            var template = await _templateDiscoveryService.GetTemplateByShortNameAsync(shortName, version);
            if (template == null)
            {
                throw await CreateTemplateNotFoundExceptionAsync(shortName, version);
            }

            // Validate parameters
            _parameterValidator.ValidateParameters(template, parameters);

            // Prepare template name
            var name = GetTemplateName(parameters, outputPath);

            // Create template
            var result = await _templateCreator.InstantiateAsync(
                templateInfo: template,
                name: name,
                fallbackName: name,
                outputPath: outputPath,
                inputParameters: new InputDataSet(template, parameters.ToDictionary(kvp => kvp.Key, kvp => (string?)kvp.Value)),
                forceCreation: false,
                baselineName: null,
                dryRun: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // Handle result
            return await HandleCreationResultAsync(result, outputPath);
        }

        private static void ValidateInputs(string shortName, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(shortName))
            {
                throw new ArgumentException(
                    "Template short name must be provided.\n\n" +
                    "💡 Corrective actions:\n" +
                    "   • Provide a valid template short name (e.g., 'webapp', 'console', etc.)\n" +
                    "   • List available templates to see valid short names", 
                    nameof(shortName));
            }
            
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException(
                    "Output path cannot be null or empty.\n\n" +
                    "💡 Corrective actions:\n" +
                    "   • Provide a valid output directory path\n" +
                    "   • Use '.' for current directory\n" +
                    "   • Use absolute or relative paths like './my-project' or '/full/path/to/project'", 
                    nameof(outputPath));
            }

            // Validate that we can create the output directory
            try
            {
                var fullOutputPath = Path.GetFullPath(outputPath);
                var parentDir = Path.GetDirectoryName(fullOutputPath);
                
                if (parentDir != null && !Directory.Exists(parentDir))
                {
                    throw new ArgumentException(
                        $"Parent directory does not exist: '{parentDir}'\n\n" +
                        $"💡 Corrective actions:\n" +
                        $"   • Create the parent directory first\n" +
                        $"   • Use an existing directory path\n" +
                        $"   • Check if you have permissions to access the path");
                }
            }
            catch (Exception ex) when (!(ex is ArgumentException))
            {
                throw new ArgumentException(
                    $"Invalid output path: '{outputPath}'\n" +
                    $"Technical details: {ex.Message}\n\n" +
                    $"💡 Corrective actions:\n" +
                    $"   • Use a valid directory path\n" +
                    $"   • Avoid invalid characters in the path\n" +
                    $"   • Check path length restrictions", 
                    nameof(outputPath), ex);
            }
        }

        private Task<InvalidOperationException> CreateTemplateNotFoundExceptionAsync(string shortName, string? version)
        {
            // Get available templates to provide helpful suggestions - we need to use the package service through dependency injection
            // For now, we'll create a simpler error message
            var errorMessage = $"Template '{shortName}' not found";
            if (version != null)
            {
                errorMessage += $" (version: {version})";
            }
            errorMessage += $".\n\n";
            
            errorMessage += $" Corrective actions:\n" +
                          $"   • Check if the template package is properly installed\n" +
                          $"   • Verify the package contains templates by listing available templates\n" +
                          $"   • Try reinstalling the template package";
            
            return Task.FromResult(new InvalidOperationException(errorMessage));
        }

        private static string? GetTemplateName(IDictionary<string, string> parameters, string outputPath)
        {
            // Get explicit name from parameters
            var name = parameters.ContainsKey("name") ? parameters["name"] : null;
            
            // If no name is provided, use the output directory name as fallback (matches dotnet CLI behavior)
            if (string.IsNullOrEmpty(name))
            {
                string? fallbackName = new DirectoryInfo(outputPath).Name;
                char[] invalidChars = Path.GetInvalidFileNameChars();
                if (string.IsNullOrEmpty(fallbackName) || string.Equals(fallbackName, "/", StringComparison.Ordinal))
                {
                    // DirectoryInfo("/").Name on *nix returns "/", as opposed to null or "".
                    fallbackName = null;
                }
                // Name returns <disk letter>:\ for root disk folder on Windows - replace invalid chars
                else if (fallbackName.IndexOfAny(invalidChars) > -1)
                {
                    System.Text.RegularExpressions.Regex pattern = new($"[{System.Text.RegularExpressions.Regex.Escape(new string(invalidChars))}]");
                    fallbackName = pattern.Replace(fallbackName, "");
                    if (string.IsNullOrWhiteSpace(fallbackName))
                    {
                        fallbackName = null;
                    }
                }
                name = fallbackName; // Use fallback name if no explicit name was provided
            }

            return name;
        }

        private Task<TemplateScaffoldResult> HandleCreationResultAsync(ITemplateCreationResult result, string outputPath)
        {
            switch (result.Status)
            {
                case CreationResultStatus.Success:
                    Console.WriteLine("Template created successfully.");
                    
                    // Execute post-actions if any using the proper PostActionDispatcher
                    var postActions = result.CreationResult?.PostActions;
                    if (postActions != null && postActions.Count > 0)
                    {
                        Console.WriteLine($"Executing {postActions.Count} post-action(s)...");
                        
                        // Use the existing PostActionDispatcher implementation
                        var dispatcher = new PostActionDispatcher(
                            _environmentSettings,
                            (permission, action) => permission == ScriptPermission.Yes // Always allow for now - can be configurable later
                        );
                        
                        // Change current directory to outputPath before running post-actions, restoring afterwards
                        // This ensures relative script paths resolve correctly
                        string originalDirectory = Directory.GetCurrentDirectory();
                        try
                        {
                            Directory.SetCurrentDirectory(outputPath);
                            var (postActionResult, failedActions) = dispatcher.RunPostActions(postActions, ScriptPermission.Yes, result, outputPath);
                            
                            if ((postActionResult & PostActionResult.Failure) != 0)
                            {
                                // Some post-actions failed but we still return success for template creation
                                Console.WriteLine($"\n⚠️  Some post-actions failed. Template was created successfully but setup may be incomplete.");
                                return Task.FromResult(new TemplateScaffoldResult { Success = true, FailedActions = failedActions });
                            }
                        }
                        finally
                        {
                            Directory.SetCurrentDirectory(originalDirectory);
                        }
                    }
                    
                    return Task.FromResult(new TemplateScaffoldResult { Success = true, FailedActions = new List<IPostAction>() });

                default:
                    throw CreateCreationFailedException(result);
            }
        }

        private static InvalidOperationException CreateCreationFailedException(ITemplateCreationResult result)
        {
            return result.Status switch
            {
                CreationResultStatus.NotFound => new InvalidOperationException(
                    $"Template not found during creation.\n" +
                    $"Details: {result.ErrorMessage}\n\n" +
                    $"💡 This should not happen after template discovery. Please report this issue."),

                CreationResultStatus.InvalidParamValues => new InvalidOperationException(
                    $"Invalid parameter values were provided.\n" +
                    $"Details: {result.ErrorMessage}\n\n" +
                    $"💡 Corrective actions:\n" +
                    $"   • Check parameter types and valid values\n" +
                    $"   • List template parameters to see requirements and valid options\n" +
                    $"   • Ensure boolean parameters use 'true' or 'false'\n" +
                    $"   • Ensure choice parameters use valid options"),

                CreationResultStatus.MissingMandatoryParam => new InvalidOperationException(
                    $"Required parameters are missing for template.\n" +
                    $"Details: {result.ErrorMessage}\n\n" +
                    $"💡 Corrective actions:\n" +
                    $"   • List template parameters to see all required parameters\n" +
                    $"   • Provide values for all required parameters"),

                CreationResultStatus.CreateFailed => new InvalidOperationException(
                    $"Template creation failed during execution.\n" +
                    $"Details: {result.ErrorMessage}\n\n" +
                    $"💡 Corrective actions:\n" +
                    $"   • Check that the output directory is writable\n" +
                    $"   • Ensure sufficient disk space is available\n" +
                    $"   • Verify no files are locked or in use in the target directory\n" +
                    $"   • Try creating in a different directory"),

                CreationResultStatus.TemplateIssueDetected => new InvalidOperationException(
                    $"The template has configuration issues.\n" +
                    $"Details: {result.ErrorMessage}\n\n" +
                    $"💡 Corrective actions:\n" +
                    $"   • This is likely a template authoring issue\n" +
                    $"   • Try updating the template package\n" +
                    $"   • Contact the template package maintainer if the issue persists"),

                CreationResultStatus.DestructiveChangesDetected => new InvalidOperationException(
                    $"Template creation would overwrite existing files.\n" +
                    $"Details: {result.ErrorMessage}\n\n" +
                    $"💡 Corrective actions:\n" +
                    $"   • Choose a different output directory\n" +
                    $"   • Backup existing files if you want to proceed\n" +
                    $"   • Use force creation option if available to overwrite existing files"),

                CreationResultStatus.Cancelled => new InvalidOperationException(
                    $"Template creation was cancelled.\n" +
                    $"Details: {result.ErrorMessage ?? "Operation was cancelled by user or system"}"),

                _ => new InvalidOperationException(
                    $"Template creation failed with status: {result.Status}\n" +
                    $"Details: {result.ErrorMessage ?? "Unknown error occurred"}\n\n" +
                    $"💡 Please report this issue if it persists.")
            };
        }
    }
}
