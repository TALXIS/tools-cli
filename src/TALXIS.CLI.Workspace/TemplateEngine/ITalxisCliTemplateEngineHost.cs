using Microsoft.TemplateEngine.Abstractions;

namespace TALXIS.CLI.Workspace.TemplateEngine
{
    /// <summary>
    /// Interface for TALXIS CLI template engine host, following the same pattern as ICliTemplateEngineHost.
    /// </summary>
    public interface ITalxisCliTemplateEngineHost : ITemplateEngineHost
    {
        /// <summary>
        /// Gets the output path for template creation.
        /// </summary>
        string OutputPath { get; }

        /// <summary>
        /// Gets a value indicating whether a custom output path was specified.
        /// </summary>
        bool IsCustomOutputPath { get; }
    }
}
