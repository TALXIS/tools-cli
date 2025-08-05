using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.Abstractions;
using TALXIS.CLI.Workspace.TemplateEngine;
using TALXIS.CLI.Workspace.TemplateEngine.Services;

namespace TALXIS.CLI.Workspace
{
    /// <summary>
    /// Template invoker that follows the official dotnet CLI patterns for template engine usage.
    /// Now implemented using focused services for better separation of concerns.
    /// </summary>
    public class TemplateInvoker : IDisposable
    {
        private readonly ITemplatePackageService _packageService;
        private readonly ITemplateDiscoveryService _discoveryService;
        private readonly ITemplateCreationService _creationService;

        public TemplateInvoker(string? outputPath = null, LogLevel logLevel = LogLevel.Error)
        {
            _packageService = TemplateEngineFactory.CreateTemplatePackageService(outputPath, logLevel);
            _discoveryService = TemplateEngineFactory.CreateTemplateDiscoveryService(outputPath, logLevel);
            _creationService = TemplateEngineFactory.CreateTemplateCreationService(outputPath, logLevel);
        }

        /// <summary>
        /// Ensures the template package is installed with the specified version.
        /// </summary>
        public async Task EnsureTemplatePackageInstalled(string? version = null) =>
            await _packageService.EnsureTemplatePackageInstalledAsync(version);

        /// <summary>
        /// Lists all available templates from the installed package.
        /// </summary>
        public async Task<List<ITemplateInfo>> ListTemplatesAsync(string? version = null) =>
            await _packageService.ListTemplatesAsync(version);

        /// <summary>
        /// Finds a template by its short name.
        /// </summary>
        public async Task<ITemplateInfo?> GetTemplateByShortNameAsync(string shortName, string? version = null) =>
            await _discoveryService.GetTemplateByShortNameAsync(shortName, version);

        /// <summary>
        /// Lists all parameters for a specific template.
        /// </summary>
        public async Task<IReadOnlyList<ITemplateParameter>> ListParametersForTemplateAsync(string shortName, string? version = null) =>
            await _discoveryService.ListParametersForTemplateAsync(shortName, version);

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
            var result = await _creationService.ScaffoldAsync(shortName, outputPath, parameters, version, cancellationToken);
            return (result.Success, result.FailedActions);
        }

        public void Dispose()
        {
            // Services created by factory don't require explicit disposal
            // The _environmentSettings will handle cleanup automatically
        }
    }
}
