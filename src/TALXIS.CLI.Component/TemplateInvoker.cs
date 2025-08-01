using Microsoft.TemplateEngine.IDE;
using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge;
using Microsoft.TemplateEngine.Edge.Template;
using Microsoft.TemplateEngine.Orchestrator.RunnableProjects;                      

namespace TALXIS.CLI.Component
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
                hostIdentifier: "TALXIS.CLI.Component",
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

        public async Task<ITemplateInfo?> GetTemplateByShortNameAsync(string shortName, string? version = null)
        {
            var templates = await ListTemplatesAsync(version);
            return templates.FirstOrDefault(t => t.ShortNameList.Contains(shortName, StringComparer.OrdinalIgnoreCase));
        }

        public async Task<IReadOnlyList<ITemplateParameter>> ListParametersForTemplateAsync(string shortName, string? version = null)
        {
            var template = await GetTemplateByShortNameAsync(shortName, version);
            if (template == null) throw new InvalidOperationException($"Template '{shortName}' not found.");
            // Use ParameterDefinitions property (non-obsolete in v9+)
            return template.ParameterDefinitions;
        }

        public async Task ScaffoldAsync(string shortName, string outputPath, IDictionary<string, string> parameters, string? version = null, CancellationToken cancellationToken = default)
        {
            await EnsureTemplatePackageInstalled(version);
            var template = await GetTemplateByShortNameAsync(shortName, version);
            if (template == null) throw new InvalidOperationException($"Template '{shortName}' not found.");
            var name = parameters.ContainsKey("name") ? parameters["name"] : "Component";

            // print template name and paerrameters
            Console.WriteLine($"Scaffolding component from template '{template.Name}' with parameters:");
            foreach (var kv in parameters)
            {
                Console.WriteLine($"  {kv.Key} = {kv.Value}");
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
                case Microsoft.TemplateEngine.Edge.Template.CreationResultStatus.Success:
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
                            dispatcher.RunPostActions(postActions, ScriptPermission.Yes);
                        }
                        finally
                        {
                            Directory.SetCurrentDirectory(originalDirectory);
                        }
                    }
                    break;
                case Microsoft.TemplateEngine.Edge.Template.CreationResultStatus.MissingMandatoryParam:
                case Microsoft.TemplateEngine.Edge.Template.CreationResultStatus.InvalidParamValues:
                    Console.Error.WriteLine($"Template creation failed due to parameter issues: {result.ErrorMessage}");
                    break;
                default:
                    Console.Error.WriteLine($"Creation failed: {result.ErrorMessage}");
                    break;
            }
        }

        public void Dispose()
        {
            _bootstrapper?.Dispose();
        }
    }
}
