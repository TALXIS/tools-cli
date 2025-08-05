using Microsoft.TemplateEngine.Abstractions;

namespace TALXIS.CLI.Workspace.TemplateEngine.Services
{
    /// <summary>
    /// Service responsible for discovering and retrieving templates.
    /// </summary>
    public interface ITemplateDiscoveryService
    {
        /// <summary>
        /// Finds a template by its short name.
        /// </summary>
        /// <param name="shortName">The short name of the template.</param>
        /// <param name="version">Optional version filter.</param>
        /// <returns>The template info if found, null otherwise.</returns>
        Task<ITemplateInfo?> GetTemplateByShortNameAsync(string shortName, string? version = null);

        /// <summary>
        /// Lists all parameters for a specific template.
        /// </summary>
        /// <param name="shortName">The short name of the template.</param>
        /// <param name="version">Optional version filter.</param>
        /// <returns>List of template parameters excluding system parameters.</returns>
        Task<IReadOnlyList<ITemplateParameter>> ListParametersForTemplateAsync(string shortName, string? version = null);
    }
}
