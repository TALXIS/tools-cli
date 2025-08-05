using Microsoft.TemplateEngine.Abstractions;

namespace TALXIS.CLI.Workspace.TemplateEngine.Services
{
    /// <summary>
    /// Service responsible for managing template packages (installation, listing, etc.)
    /// </summary>
    public interface ITemplatePackageService
    {
        /// <summary>
        /// Ensures the template package is installed with the specified version.
        /// </summary>
        /// <param name="version">Optional version to install. If null, installs latest.</param>
        /// <returns>Task that completes when package is installed.</returns>
        Task EnsureTemplatePackageInstalledAsync(string? version = null);

        /// <summary>
        /// Lists all available templates from the installed package.
        /// </summary>
        /// <param name="version">Optional version filter.</param>
        /// <returns>List of available templates.</returns>
        Task<List<ITemplateInfo>> ListTemplatesAsync(string? version = null);

        /// <summary>
        /// Gets the configured template package name.
        /// </summary>
        string TemplatePackageName { get; }
    }
}
