using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Edge;
using Microsoft.TemplateEngine.Edge.Settings;
using Microsoft.TemplateEngine.Edge.Template;
using Microsoft.TemplateEngine.Utils;
using TALXIS.CLI.Workspace.TemplateEngine;
using CreationResultStatus = Microsoft.TemplateEngine.Edge.Template.CreationResultStatus;

namespace TALXIS.CLI.Workspace
{
    /// <summary>
    /// Template invoker that follows the official dotnet CLI patterns for template engine usage.
    /// </summary>
    public class TemplateInvoker : IDisposable
    {
        private readonly IEngineEnvironmentSettings _environmentSettings;
        private readonly ITalxisCliTemplateEngineHost _host;
        private readonly TemplateCreator _templateCreator;
        private readonly TemplatePackageManager _templatePackageManager;
        private readonly string _templatePackageName = "TALXIS.DevKit.Templates.Dataverse";
        private bool _isTemplateInstalled = false;

        public TemplateInvoker()
        {
            var version = typeof(TemplateInvoker).Assembly.GetName().Version?.ToString() ?? "1.0.0.0";
            
            // Create the host following official patterns
            var builtIns = new List<(Type, IIdentifiedComponent)>();
            builtIns.AddRange(Microsoft.TemplateEngine.Edge.Components.AllComponents);
            
            _host = new TalxisCliTemplateEngineHost(
                hostIdentifier: "TALXIS.CLI.Workspace",
                version: version,
                preferences: new Dictionary<string, string>
                {
                    ["allow-scripts"] = "yes"
                },
                builtIns: builtIns,
                fallbackHostNames: new[] { "talxis-cli" },
                logLevel: LogLevel.Error);

            // Create environment settings following official patterns
            _environmentSettings = new EngineEnvironmentSettings(_host, virtualizeSettings: false);
            
            // Initialize template creator and package manager
            _templateCreator = new TemplateCreator(_environmentSettings);
            _templatePackageManager = new TemplatePackageManager(_environmentSettings);
        }

        public async Task EnsureTemplatePackageInstalled(string? version = null)
        {
            if (_isTemplateInstalled) return;
            
            try
            {
                // Get the managed template package provider
                var managedProvider = _templatePackageManager.GetBuiltInManagedProvider(InstallationScope.Global);
                var packageId = _templatePackageName + (version != null ? $"::{version}" : "");
                var installRequests = new[] { new InstallRequest(packageId) };
                
                var results = await managedProvider.InstallAsync(installRequests, CancellationToken.None);
                if (results.Any(r => !r.Success))
                {
                    var failedResults = results.Where(r => !r.Success);
                    var detailedErrors = string.Join("\n", failedResults.Select(r => 
                        $"   • Error: {r.ErrorMessage}"));
                    
                    var userErrorMessage = $"Failed to install template package '{packageId}'.\n" +
                                         $"Details:\n{detailedErrors}\n\n" +
                                         $"💡 Corrective actions:\n" +
                                         $"   • Check your internet connection\n" +
                                         $"   • Verify the package name and version are correct\n" +
                                         $"   • Ensure you have sufficient permissions for global package installation\n" +
                                         $"   • If using a private package source, ensure it's properly configured";
                    
                    throw new InvalidOperationException(userErrorMessage);
                }
                
                _isTemplateInstalled = true;
            }
            catch (Exception ex) when (!(ex is InvalidOperationException))
            {
                // Wrap unexpected exceptions with user-friendly message
                var userErrorMessage = $"Unexpected error while installing template package '{_templatePackageName}'{(version != null ? $" version {version}" : "")}.\n" +
                                     $"Technical details: {ex.Message}\n\n" +
                                     $"💡 Corrective actions:\n" +
                                     $"   • Check your internet connection\n" +
                                     $"   • Ensure you have permission to install global packages\n" +
                                     $"   • Check if the package source is accessible";
                
                throw new InvalidOperationException(userErrorMessage, ex);
            }
        }

        public async Task<List<ITemplateInfo>> ListTemplatesAsync(string? version = null)
        {
            await EnsureTemplatePackageInstalled(version);
            var templates = await _templatePackageManager.GetTemplatesAsync(CancellationToken.None);
            return templates.Where(t => t.MountPointUri.Contains(_templatePackageName, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private static void ValidateShortName(string shortName)
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
        }

        private static void ValidateParametersWithDetailedErrors(ITemplateInfo template, IDictionary<string, string> userParameters)
        {
            var errors = new List<string>();
            
            // Get only user-input parameters (exclude system parameters like name, type, language)
            var templateParameters = template.ParameterDefinitions
                .Where(p => p.Name != "type" && p.Name != "language" && p.Name != "name")
                .ToList();

            foreach (var templateParam in templateParameters)
            {
                var paramName = templateParam.Name;
                var hasUserValue = userParameters.ContainsKey(paramName);
                var userValue = hasUserValue ? userParameters[paramName] : null;

                // Check required parameters
                var isRequired = templateParam.Precedence?.ToString() == "Required";
                if (isRequired && string.IsNullOrWhiteSpace(userValue))
                {
                    errors.Add($"Parameter '{paramName}' is required but was not provided.");
                    continue; // Skip further validation for missing required parameters
                }

                // Skip validation if no value provided and parameter is optional
                if (!hasUserValue || string.IsNullOrWhiteSpace(userValue))
                {
                    continue;
                }

                // Validate parameter value with detailed error messages
                ValidateParameterValueWithDetailedErrors(templateParam, userValue, errors);
            }

            if (errors.Count > 0)
            {
                throw new ArgumentException($"Parameter validation failed:\n{string.Join("\n", errors)}");
            }
        }

        private static void ValidateParameterValueWithDetailedErrors(ITemplateParameter templateParam, string userValue, List<string> errors)
        {
            var paramName = templateParam.Name;

            // Validate based on data type
            switch (templateParam.DataType?.ToLowerInvariant())
            {
                case "bool":
                case "boolean":
                    if (!bool.TryParse(userValue, out _))
                    {
                        errors.Add($"Parameter '{paramName}' expects a boolean value (true/false), but got '{userValue}'.");
                    }
                    break;

                case "int":
                case "integer":
                    if (!int.TryParse(userValue, out _))
                    {
                        errors.Add($"Parameter '{paramName}' expects an integer value, but got '{userValue}'.");
                    }
                    break;

                case "choice":
                    // Validate against allowed choices with detailed error message
                    if (templateParam.Choices != null && templateParam.Choices.Count > 0)
                    {
                        var validChoices = templateParam.Choices.Keys.ToList();

                        if (!validChoices.Contains(userValue, StringComparer.OrdinalIgnoreCase))
                        {
                            var choicesStr = string.Join(", ", validChoices.Select(c => $"'{c}'"));
                            errors.Add($"Parameter '{paramName}' must be one of: {choicesStr}. Got '{userValue}'.");
                        }
                    }
                    break;

                case "text":
                case "string":
                    // Text parameters generally don't need additional validation beyond required check
                    // Could add length validation here if needed
                    break;

                default:
                    // For unknown data types, just log a warning but don't fail validation
                    Console.WriteLine($"Warning: Unknown data type '{templateParam.DataType}' for parameter '{paramName}'. Skipping validation.");
                    break;
            }
        }

        // Note: Parameter validation is handled by the Microsoft.TemplateEngine automatically
        // The engine validates data types, choices, and required parameters based on template.json
        // This follows the same pattern as the official .NET CLI

        public async Task<ITemplateInfo?> GetTemplateByShortNameAsync(string shortName, string? version = null)
        {
            ValidateShortName(shortName);

            var templates = await ListTemplatesAsync(version);
            return templates.FirstOrDefault(t => t.ShortNameList.Contains(shortName, StringComparer.OrdinalIgnoreCase));
        }

        public async Task<IReadOnlyList<ITemplateParameter>> ListParametersForTemplateAsync(string shortName, string? version = null)
        {
            ValidateShortName(shortName);

            var template = await GetTemplateByShortNameAsync(shortName, version);
            if (template == null) throw new InvalidOperationException($"Template '{shortName}' not found.");

            // Return only the template-defined parameters, excluding type and language (matches dotnet CLI behavior)
            // Note: 'name' and 'output' are CLI-level options, not template parameters, so they should not be listed here
            return template.ParameterDefinitions.Where(p => p.Name != "type" && p.Name != "language").ToList();
        }

        public async Task<(bool Success, List<IPostAction> FailedActions)> ScaffoldAsync(string shortName, string outputPath, IDictionary<string, string> parameters, string? version = null, CancellationToken cancellationToken = default)
        {
            ValidateShortName(shortName);
            
            // Validate output path
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

            await EnsureTemplatePackageInstalled(version);
            var template = await GetTemplateByShortNameAsync(shortName, version);
            if (template == null) 
            {
                // Get available templates to provide helpful suggestions
                var availableTemplates = await ListTemplatesAsync(version);
                var templateNames = availableTemplates.SelectMany(t => t.ShortNameList).Distinct().ToList();
                
                var errorMessage = $"Template '{shortName}' not found";
                if (version != null)
                {
                    errorMessage += $" (version: {version})";
                }
                errorMessage += $" in package '{_templatePackageName}'.\n\n";
                
                if (templateNames.Any())
                {
                    errorMessage += $"📋 Available templates:\n";
                    foreach (var templateName in templateNames.Take(10)) // Show first 10
                    {
                        errorMessage += $"   • {templateName}\n";
                    }
                    if (templateNames.Count > 10)
                    {
                        errorMessage += $"   ... and {templateNames.Count - 10} more\n";
                    }
                    errorMessage += $"\n💡 List available templates to see all options with descriptions";
                }
                else
                {
                    errorMessage += $"💡 Corrective actions:\n" +
                                  $"   • Check if the template package is properly installed\n" +
                                  $"   • Verify the package contains templates by listing available templates\n" +
                                  $"   • Try reinstalling the template package: {_templatePackageName}";
                }
                
                throw new InvalidOperationException(errorMessage);
            }

            // Validate parameters with detailed error messages before attempting creation
            ValidateParametersWithDetailedErrors(template, parameters);
            
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

            // Use the modern TemplateCreator API instead of legacy Bootstrapper  
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

            // Handle template creation results with proper success and error handling
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
                            var (postActionResult, failedActions) = dispatcher.RunPostActions(postActions, ScriptPermission.Yes);
                            
                            if ((postActionResult & PostActionResult.Failure) != 0)
                            {
                                // Some post-actions failed but we still return success for template creation
                                Console.WriteLine($"\n⚠️  Some post-actions failed. Template was created successfully but setup may be incomplete.");
                                return (Success: true, FailedActions: failedActions);
                            }
                        }
                        finally
                        {
                            Directory.SetCurrentDirectory(originalDirectory);
                        }
                    }
                    
                    return (Success: true, FailedActions: new List<IPostAction>());

                case CreationResultStatus.NotFound:
                    // Get available templates to provide helpful suggestions
                    var availableTemplates = await ListTemplatesAsync(version);
                    var templateNames = availableTemplates.SelectMany(t => t.ShortNameList).Distinct().ToList();
                    
                    var notFoundMessage = $"Template '{shortName}' not found";
                    if (version != null)
                    {
                        notFoundMessage += $" (version: {version})";
                    }
                    notFoundMessage += $" in package '{_templatePackageName}'.\n\n";
                    
                    if (templateNames.Any())
                    {
                        notFoundMessage += $"📋 Available templates:\n";
                        foreach (var templateName in templateNames.Take(10)) // Show first 10
                        {
                            notFoundMessage += $"   • {templateName}\n";
                        }
                        if (templateNames.Count > 10)
                        {
                            notFoundMessage += $"   ... and {templateNames.Count - 10} more\n";
                        }
                        notFoundMessage += $"\n💡 List available templates to see all options with descriptions";
                    }
                    else
                    {
                        notFoundMessage += $"💡 Corrective actions:\n" +
                                          $"   • Check if the template package is properly installed\n" +
                                          $"   • Verify the package contains templates by listing available templates\n" +
                                          $"   • Try reinstalling the template package: {_templatePackageName}";
                    }
                    
                    throw new InvalidOperationException(notFoundMessage);

                case CreationResultStatus.InvalidParamValues:
                    throw new InvalidOperationException(
                        $"Invalid parameter values were provided.\n" +
                        $"Details: {result.ErrorMessage}\n\n" +
                        $"💡 Corrective actions:\n" +
                        $"   • Check parameter types and valid values\n" +
                        $"   • List template parameters to see requirements and valid options\n" +
                        $"   • Ensure boolean parameters use 'true' or 'false'\n" +
                        $"   • Ensure choice parameters use valid options");

                case CreationResultStatus.MissingMandatoryParam:
                    throw new InvalidOperationException(
                        $"Required parameters are missing for template '{shortName}'.\n" +
                        $"Details: {result.ErrorMessage}\n\n" +
                        $"💡 Corrective actions:\n" +
                        $"   • List template parameters to see all required parameters\n" +
                        $"   • Provide values for all required parameters");

                case CreationResultStatus.CreateFailed:
                    throw new InvalidOperationException(
                        $"Template creation failed during execution.\n" +
                        $"Details: {result.ErrorMessage}\n\n" +
                        $"💡 Corrective actions:\n" +
                        $"   • Check that the output directory is writable: '{outputPath}'\n" +
                        $"   • Ensure sufficient disk space is available\n" +
                        $"   • Verify no files are locked or in use in the target directory\n" +
                        $"   • Try creating in a different directory");

                case CreationResultStatus.TemplateIssueDetected:
                    throw new InvalidOperationException(
                        $"The template '{shortName}' has configuration issues.\n" +
                        $"Details: {result.ErrorMessage}\n\n" +
                        $"💡 Corrective actions:\n" +
                        $"   • This is likely a template authoring issue\n" +
                        $"   • Try updating the template package: {_templatePackageName}\n" +
                        $"   • Contact the template package maintainer if the issue persists");

                case CreationResultStatus.DestructiveChangesDetected:
                    throw new InvalidOperationException(
                        $"Template creation would overwrite existing files.\n" +
                        $"Details: {result.ErrorMessage}\n\n" +
                        $"💡 Corrective actions:\n" +
                        $"   • Choose a different output directory\n" +
                        $"   • Backup existing files if you want to proceed\n" +
                        $"   • Use force creation option if available to overwrite existing files");

                case CreationResultStatus.Cancelled:
                    throw new InvalidOperationException(
                        $"Template creation was cancelled.\n" +
                        $"Details: {result.ErrorMessage ?? "Operation was cancelled by user or system"}");

                default:
                    throw new InvalidOperationException(
                        $"Template creation failed with status: {result.Status}\n" +
                        $"Details: {result.ErrorMessage ?? "Unknown error occurred"}\n\n" +
                        $"💡 Please report this issue if it persists.");
            }
        }

        public void Dispose()
        {
            // Modern TemplateCreator and other components don't require explicit disposal
            // The _environmentSettings will handle cleanup automatically
        }
    }
}
