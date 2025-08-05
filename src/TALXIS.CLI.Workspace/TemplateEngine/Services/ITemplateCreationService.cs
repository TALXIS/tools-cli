using Microsoft.TemplateEngine.Abstractions;

namespace TALXIS.CLI.Workspace.TemplateEngine.Services
{
    /// <summary>
    /// Result of template scaffolding operation.
    /// </summary>
    public class TemplateScaffoldResult
    {
        public bool Success { get; set; }
        public List<IPostAction> FailedActions { get; set; } = new();
    }

    /// <summary>
    /// Service responsible for creating templates from template definitions.
    /// </summary>
    public interface ITemplateCreationService
    {
        /// <summary>
        /// Scaffolds a template to the specified output path with the given parameters.
        /// </summary>
        /// <param name="shortName">The short name of the template.</param>
        /// <param name="outputPath">The path where the template should be created.</param>
        /// <param name="parameters">Parameters to pass to the template.</param>
        /// <param name="version">Optional version of the template package.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Result of the scaffolding operation.</returns>
        Task<TemplateScaffoldResult> ScaffoldAsync(
            string shortName, 
            string outputPath, 
            IDictionary<string, string> parameters, 
            string? version = null, 
            CancellationToken cancellationToken = default);
    }
}
