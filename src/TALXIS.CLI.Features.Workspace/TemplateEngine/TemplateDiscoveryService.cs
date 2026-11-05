using Microsoft.TemplateEngine.Abstractions;

namespace TALXIS.CLI.Features.Workspace.TemplateEngine
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

            return SelectUserParameters(template.ParameterDefinitions);
        }

        /// <summary>
        /// Drops the host-level 'type' and 'language' parameters and keeps everything else, including 'name'.
        /// </summary>
        internal static IReadOnlyList<ITemplateParameter> SelectUserParameters(IEnumerable<ITemplateParameter> definitions)
        {
            // 'name' replaces the template's sourceName and is set with --param name=..., so it must stay listed
            return definitions.Where(p => p.Name != "type" && p.Name != "language").ToList();
        }

        private static void ValidateShortName(string shortName)
        {
            if (string.IsNullOrWhiteSpace(shortName))
            {
                throw new ArgumentException(
                    "Component type must be provided.\n\n" +
                    "💡 Corrective actions:\n" +
                    "   • Provide a valid component type\n" +
                    "   • List available component types to see valid names",
                    nameof(shortName));
            }
        }
    }
}
