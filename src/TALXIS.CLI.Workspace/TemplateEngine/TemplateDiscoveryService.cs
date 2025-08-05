using Microsoft.TemplateEngine.Abstractions;

namespace TALXIS.CLI.Workspace.TemplateEngine
{
    /// <summary>
    /// Service responsible for discovering and retrieving templates.
    /// </summary>
    public class TemplateDiscoveryService
    {
        private readonly TemplatePackageService _templatePackageService;

        public TemplateDiscoveryService(TemplatePackageService templatePackageService)
        {
            _templatePackageService = templatePackageService ?? throw new ArgumentNullException(nameof(templatePackageService));
        }

        public async Task<ITemplateInfo?> GetTemplateByShortNameAsync(string shortName, string? version = null)
        {
            ValidateShortName(shortName);

            var templates = await _templatePackageService.ListTemplatesAsync(version);
            return templates.FirstOrDefault(t => t.ShortNameList.Contains(shortName, StringComparer.OrdinalIgnoreCase));
        }

        public async Task<IReadOnlyList<ITemplateParameter>> ListParametersForTemplateAsync(string shortName, string? version = null)
        {
            ValidateShortName(shortName);

            var template = await GetTemplateByShortNameAsync(shortName, version);
            if (template == null) 
                throw new InvalidOperationException($"Template '{shortName}' not found.");

            // Return only the template-defined parameters, excluding type, language, and name (matches dotnet CLI behavior)
            // Note: 'name' and 'output' are CLI-level options, not template parameters, so they should not be listed here
            return template.ParameterDefinitions.Where(p => p.Name != "type" && p.Name != "language" && p.Name != "name").ToList();
        }

        private static void ValidateShortName(string shortName)
        {
            if (string.IsNullOrWhiteSpace(shortName))
            {
                throw new ArgumentException(
                    "Template short name must be provided.\n\n" +
                    "💡 Corrective actions:\n" +
                    "   • Provide a valid template short name\n" +
                    "   • List available templates to see valid short names", 
                    nameof(shortName));
            }
        }
    }
}
