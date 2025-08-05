using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge;
using Microsoft.TemplateEngine.Edge.Settings;
using Microsoft.TemplateEngine.Edge.Template;
using TALXIS.CLI.Workspace.TemplateEngine.Services;

namespace TALXIS.CLI.Workspace.TemplateEngine
{
    /// <summary>
    /// Factory for creating and configuring template engine services.
    /// </summary>
    public class TemplateEngineFactory
    {
        /// <summary>
        /// Creates a complete template engine setup with all required services.
        /// </summary>
        /// <param name="outputPath">Optional custom output path for template creation.</param>
        /// <param name="logLevel">Log level for the template engine host.</param>
        /// <returns>A configured template creation service.</returns>
        public static ITemplateCreationService CreateTemplateCreationService(
            string? outputPath = null, 
            LogLevel logLevel = LogLevel.Error)
        {
            // Create template engine host
            var host = CreateTemplateEngineHost(outputPath, logLevel);
            
            // Create environment settings
            var environmentSettings = new EngineEnvironmentSettings(host, virtualizeSettings: false);
            
            // Create template infrastructure
            var templateCreator = new TemplateCreator(environmentSettings);
            var templatePackageManager = new TemplatePackageManager(environmentSettings);
            
            // Create services
            var packageService = new TemplatePackageService(templatePackageManager);
            var discoveryService = new TemplateDiscoveryService(packageService);
            var parameterValidator = new TemplateParameterValidator();
            
            return new TemplateCreationService(
                discoveryService,
                parameterValidator,
                templateCreator,
                environmentSettings);
        }

        /// <summary>
        /// Creates template package service only.
        /// </summary>
        /// <param name="outputPath">Optional custom output path for template creation.</param>
        /// <param name="logLevel">Log level for the template engine host.</param>
        /// <returns>A configured template package service.</returns>
        public static ITemplatePackageService CreateTemplatePackageService(
            string? outputPath = null, 
            LogLevel logLevel = LogLevel.Error)
        {
            var host = CreateTemplateEngineHost(outputPath, logLevel);
            var environmentSettings = new EngineEnvironmentSettings(host, virtualizeSettings: false);
            var templatePackageManager = new TemplatePackageManager(environmentSettings);
            
            return new TemplatePackageService(templatePackageManager);
        }

        /// <summary>
        /// Creates template discovery service.
        /// </summary>
        /// <param name="outputPath">Optional custom output path for template creation.</param>
        /// <param name="logLevel">Log level for the template engine host.</param>
        /// <returns>A configured template discovery service.</returns>
        public static ITemplateDiscoveryService CreateTemplateDiscoveryService(
            string? outputPath = null, 
            LogLevel logLevel = LogLevel.Error)
        {
            var packageService = CreateTemplatePackageService(outputPath, logLevel);
            return new TemplateDiscoveryService(packageService);
        }

        private static ITalxisCliTemplateEngineHost CreateTemplateEngineHost(
            string? outputPath = null, 
            LogLevel logLevel = LogLevel.Error)
        {
            var version = typeof(TemplateEngineFactory).Assembly.GetName().Version?.ToString() ?? "1.0.0.0";
            
            // Create the host following official patterns
            var builtIns = new List<(Type, IIdentifiedComponent)>();
            builtIns.AddRange(Microsoft.TemplateEngine.Edge.Components.AllComponents);
            
            return new TalxisCliTemplateEngineHost(
                hostIdentifier: "TALXIS.CLI.Workspace",
                version: version,
                preferences: new Dictionary<string, string>
                {
                    ["allow-scripts"] = "yes"
                },
                builtIns: builtIns,
                fallbackHostNames: new[] { "talxis-cli" },
                outputPath: outputPath,
                logLevel: logLevel);
        }
    }
}
