using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.TemplateEngine.IDE;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Edge;
using Microsoft.TemplateEngine.Orchestrator.RunnableProjects;
using Microsoft.Extensions.Logging.Abstractions;

namespace TALXIS.CLI.Component
{
    public class ComponentScaffolder : IDisposable
    {
        private readonly Bootstrapper _bootstrapper;
        private readonly string _templatePackageName = "TALXIS.DevKit.Templates.Dataverse";
        private bool _isTemplateInstalled = false;

        public ComponentScaffolder()
        {
            var version = typeof(ComponentScaffolder).Assembly.GetName().Version?.ToString() ?? "1.0.0.0";
            var host = new DefaultTemplateEngineHost("TALXIS.CLI.Component", version);
            _bootstrapper = new Bootstrapper(
                host,
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
            await _bootstrapper.CreateAsync(
                template,
                name: name,
                outputPath: outputPath,
                parameters: parameters.ToDictionary(kv => kv.Key, kv => (string?)kv.Value),
                baselineName: null,
                cancellationToken: cancellationToken);
        }

        public void Dispose()
        {
            _bootstrapper?.Dispose();
        }
    }
}
