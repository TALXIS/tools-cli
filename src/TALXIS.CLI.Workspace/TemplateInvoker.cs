using Microsoft.TemplateEngine.IDE;
using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge;
using Microsoft.TemplateEngine.Edge.Template;

namespace TALXIS.CLI.Workspace
{
    public class TemplateInvoker : IDisposable
    {
        private readonly Bootstrapper _bootstrapper;
        private readonly string _templatePackageName = "TALXIS.DevKit.Templates.Dataverse";
        private bool _isTemplateInstalled = false;
        private DefaultTemplateEngineHost _host;

        private ILoggerFactory _loggerFactory;

        public TemplateInvoker()
        {
            var version = typeof(TemplateInvoker).Assembly.GetName().Version?.ToString() ?? "1.0.0.0";
            _loggerFactory = LoggerFactory.Create(b =>
            {
                b.SetMinimumLevel(LogLevel.Error)
                 .AddConsole();
            });
            _host = new DefaultTemplateEngineHost(
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
            // Return the list of parameters defined in the template and exclude name, type and language as they are not user-defined parameters
            return template.ParameterDefinitions.Where(p => p.Name != "name" && p.Name != "type" && p.Name != "language").ToList();
        }

        public async Task<(bool Success, List<IPostAction> FailedActions)> ScaffoldAsync(string shortName, string outputPath, IDictionary<string, string> parameters, string? version = null, CancellationToken cancellationToken = default)
        {
            ValidateShortName(shortName);

            await EnsureTemplatePackageInstalled(version);
            var template = await GetTemplateByShortNameAsync(shortName, version);
            if (template == null) throw new InvalidOperationException($"Template '{shortName}' not found.");

            var name = parameters.ContainsKey("name") ? parameters["name"] : null;
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
                case CreationResultStatus.InvalidParamValues:
                    throw new InvalidOperationException($"Template creation failed due to parameter issues: {result.ErrorMessage}");
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
