using Microsoft.TemplateEngine.IDE;
using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge;
using Microsoft.TemplateEngine.Edge.Template;
using Microsoft.TemplateEngine.Utils;

namespace TALXIS.CLI.Workspace
{
    public class TemplateInvoker : IDisposable
    {
        private readonly Bootstrapper _bootstrapper;
        private readonly string _templatePackageName = "TALXIS.DevKit.Templates.Dataverse";
        private bool _isTemplateInstalled = false;
        private Microsoft.TemplateEngine.Edge.DefaultTemplateEngineHost _host;

        private ILoggerFactory _loggerFactory;

        public TemplateInvoker()
        {
            var version = typeof(TemplateInvoker).Assembly.GetName().Version?.ToString() ?? "1.0.0.0";
            _loggerFactory = LoggerFactory.Create(b =>
            {
                b.SetMinimumLevel(LogLevel.Error)
                 .AddConsole();
            });
            _host = new Microsoft.TemplateEngine.Edge.DefaultTemplateEngineHost(
                hostIdentifier: "TALXIS.CLI.Workspace",
                version: version,
                defaults: new Dictionary<string, string>
                {
                    ["allow-scripts"] = "yes"
                },
                builtIns: null,
                fallbackHostTemplateConfigNames: null,
                loggerFactory: _loggerFactory);

            _bootstrapper = new Bootstrapper(
                _host,
                loadDefaultComponents: true,
                virtualizeConfiguration: false,
                environment: null);
        }

        public async Task EnsureTemplatePackageInstalled(string? version = null)
        {
            if (_isTemplateInstalled) return;
            var packageId = _templatePackageName + (version != null ? $"::{version}" : "");
            var installRequests = new[] { new Microsoft.TemplateEngine.Abstractions.Installer.InstallRequest(packageId) };
            await _bootstrapper.InstallTemplatePackagesAsync(installRequests);
            _isTemplateInstalled = true;
        }

        public async Task<List<ITemplateInfo>> ListTemplatesAsync(string? version = null)
        {
            await EnsureTemplatePackageInstalled(version);
            var templates = await _bootstrapper.GetTemplatesAsync(CancellationToken.None);
            return templates.Where(t => t.MountPointUri.Contains(_templatePackageName, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private static void ValidateShortName(string shortName)
        {
            if (string.IsNullOrWhiteSpace(shortName))
            {
                throw new ArgumentException("Short name must be provided", nameof(shortName));
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
                throw new ArgumentException("Output path cannot be null or empty", nameof(outputPath));
            }

            await EnsureTemplatePackageInstalled(version);
            var template = await GetTemplateByShortNameAsync(shortName, version);
            if (template == null) throw new InvalidOperationException($"Template '{shortName}' not found.");

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

            var result = await _bootstrapper.CreateAsync(
                template,
                name: name,
                outputPath: outputPath,
                parameters: parameters.ToDictionary(kv => kv.Key, kv => (string?)kv.Value),
                baselineName: null,
                cancellationToken: cancellationToken);

            switch (result.Status)
            {
                case CreationResultStatus.Success:
                    Console.WriteLine("Template created.");
                    // Execute post-actions if any
                    var postActions = result.CreationResult?.PostActions;
                    if (postActions != null && postActions.Count > 0)
                    {
                        Console.WriteLine($"Executing {postActions.Count} post-action(s)...");
                        IEngineEnvironmentSettings env = new EngineEnvironmentSettings(_host);
                        var dispatcher = new PostActionDispatcher(
                            env,
                            (permission, action) => permission == ScriptPermission.Yes // always allow for now
                        );
                        // Change current directory to outputPath before running post-actions, restoring afterwards, so relative script paths resolve.
                        string originalDirectory = Directory.GetCurrentDirectory();
                        try
                        {
                            Directory.SetCurrentDirectory(outputPath);
                            var (postActionResult, failedActions) = dispatcher.RunPostActions(postActions, ScriptPermission.Yes);
                            if ((postActionResult & PostActionResult.Failure) != 0)
                            {
                                return (false, failedActions);
                            }
                        }
                        finally
                        {
                            Directory.SetCurrentDirectory(originalDirectory);
                        }
                    }
                    return (true, new List<IPostAction>());
                case CreationResultStatus.MissingMandatoryParam:
                    // Extract parameter details from the error message if possible
                    var missingParamError = !string.IsNullOrEmpty(result.ErrorMessage) 
                        ? $"Missing required parameters: {result.ErrorMessage}" 
                        : "One or more required parameters are missing.";
                    throw new InvalidOperationException(missingParamError);
                case CreationResultStatus.InvalidParamValues:
                    // Extract parameter details from the error message if possible
                    var invalidParamError = !string.IsNullOrEmpty(result.ErrorMessage) 
                        ? $"Invalid parameter values: {result.ErrorMessage}" 
                        : "One or more parameters have invalid values.";
                    throw new InvalidOperationException(invalidParamError);
                default:
                    throw new InvalidOperationException($"Creation failed: {result.ErrorMessage}");
            }
        }

        public void Dispose()
        {
            _bootstrapper?.Dispose();
        }
    }
}
