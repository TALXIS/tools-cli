using Microsoft.TemplateEngine.Abstractions;

namespace TALXIS.CLI.Workspace.TemplateEngine
{
    /// <summary>
    /// The interface defining the post action processor supported by txc CLI.
    /// Aligns with the official .NET SDK IPostActionProcessor pattern.
    /// </summary>
    public interface IPostActionProcessor
    {
        /// <summary>
        /// Gets the unique identifier for this post action processor.
        /// </summary>
        Guid ActionId { get; }

        /// <summary>
        /// Processes the post action.
        /// </summary>
        /// <param name="environment">template engine environment settings.</param>
        /// <param name="action">the post action to process as returned by generator.</param>
        /// <returns>true if the post action is executed successfully, false otherwise.</returns>
        bool Process(IEngineEnvironmentSettings environment, IPostAction action);

        /// <summary>
        /// Processes the post action with explicit output path (if supported).
        /// </summary>
        /// <param name="environment">template engine environment settings.</param>
        /// <param name="action">the post action to process as returned by generator.</param>
        /// <param name="creationEffects">the results of the template dry run.</param>
        /// <param name="templateCreationResult">the results of the template instantiation.</param>
        /// <param name="outputBasePath">the output directory the template was instantiated to.</param>
        /// <returns>true if the post action is executed successfully, false otherwise.</returns>
        bool ProcessInternal(IEngineEnvironmentSettings environment, IPostAction action, ICreationEffects creationEffects, ICreationResult? templateCreationResult, string outputBasePath)
        {
            // Default implementation falls back to the original method
            return Process(environment, action);
        }
    }
}
