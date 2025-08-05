using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.Abstractions;
using TALXIS.CLI.Workspace.TemplateEngine.Services;

namespace TALXIS.CLI.Workspace
{
    /// <summary>
    /// Template invoker that uses the new service-based architecture for template engine operations.
    /// This provides a clean facade over the sophisticated service layer implemented during refactoring.
    /// </summary>
    public class TemplateInvoker : IDisposable
    {
        private readonly ITemplateCreationService _templateCreationService;
        private readonly ITemplateDiscoveryService _templateDiscoveryService;
        private readonly ITemplatePackageService _templatePackageService;

        public TemplateInvoker(string? outputPath = null, LogLevel logLevel = LogLevel.Error)
        {
            // Use the factory to create the service-based architecture
            _templateCreationService = TemplateEngine.TemplateEngineFactory.CreateTemplateCreationService(outputPath, logLevel);
            _templateDiscoveryService = TemplateEngine.TemplateEngineFactory.CreateTemplateDiscoveryService(outputPath, logLevel);
            _templatePackageService = TemplateEngine.TemplateEngineFactory.CreateTemplatePackageService(outputPath, logLevel);
        }

        /// <summary>
        /// Ensures the template package is installed with the specified version.
        /// </summary>
        public async Task EnsureTemplatePackageInstalled(string? version = null)
        {
            await _templatePackageService.EnsureTemplatePackageInstalledAsync(version);
        }

        /// <summary>
        /// Lists all available templates from the installed package.
        /// </summary>
        public async Task<List<ITemplateInfo>> ListTemplatesAsync(string? version = null)
        {
            return await _templatePackageService.ListTemplatesAsync(version);
        }

        /// <summary>
        /// Finds a template by its short name.
        /// </summary>
        public async Task<ITemplateInfo?> GetTemplateByShortNameAsync(string shortName, string? version = null)
        {
            return await _templateDiscoveryService.GetTemplateByShortNameAsync(shortName, version);
        }

        /// <summary>
        /// Lists all parameters for a specific template.
        /// </summary>
        public async Task<IReadOnlyList<ITemplateParameter>> ListParametersForTemplateAsync(string shortName, string? version = null)
        {
            return await _templateDiscoveryService.ListParametersForTemplateAsync(shortName, version);
        }

        /// <summary>
        /// Scaffolds a template to the specified output path with the given parameters.
        /// </summary>
        public async Task<(bool Success, List<IPostAction> FailedActions)> ScaffoldAsync(
            string shortName, 
            string outputPath, 
            IDictionary<string, string> parameters, 
            string? version = null, 
            CancellationToken cancellationToken = default)
        {
            var result = await _templateCreationService.ScaffoldAsync(shortName, outputPath, parameters, version, cancellationToken);
            return (result.Success, result.FailedActions);
        }

        public void Dispose()
        {
            // Services are created via factory and don't need explicit disposal
            // The underlying template engine components handle their own cleanup
        }
    }
}
