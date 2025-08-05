using Microsoft.TemplateEngine.Abstractions;

namespace TALXIS.CLI.Workspace.TemplateEngine.Services
{
    /// <summary>
    /// Service responsible for validating template parameters.
    /// </summary>
    public interface ITemplateParameterValidator
    {
        /// <summary>
        /// Validates user parameters against template parameter definitions.
        /// </summary>
        /// <param name="template">The template containing parameter definitions.</param>
        /// <param name="userParameters">User-provided parameters to validate.</param>
        /// <exception cref="ArgumentException">Thrown when validation fails with detailed error messages.</exception>
        void ValidateParameters(ITemplateInfo template, IDictionary<string, string> userParameters);
    }
}
