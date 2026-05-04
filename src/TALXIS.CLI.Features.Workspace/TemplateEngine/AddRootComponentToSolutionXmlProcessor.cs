using System.Xml;
using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.Abstractions;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Workspace.TemplateEngine
{
    /// <summary>
    /// Post-action processor that appends a RootComponent element to the nearest Solution.xml.
    /// Used by item templates (pp-entity, pp-form, etc.) that need to register components
    /// in the parent solution's Solution.xml after template instantiation.
    /// </summary>
    public class AddRootComponentToSolutionXmlProcessor : IPostActionProcessor
    {
        private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(AddRootComponentToSolutionXmlProcessor));
        public static Guid ActionProcessorId => new Guid("A1B2C3D4-1001-4000-8000-000000000001");

        public bool Process(IEngineEnvironmentSettings environment, IPostAction action)
        {
            return ProcessInternal(environment, action, null!, null!, System.Environment.CurrentDirectory);
        }

        public bool ProcessInternal(IEngineEnvironmentSettings environment, IPostAction action, ICreationEffects creationEffects, ICreationResult? templateCreationResult, string outputBasePath)
        {
            var args = action.Args;

            if (!args.TryGetValue("type", out var componentType))
            {
                _logger.LogError("[AddRootComponent] Missing required 'type' argument");
                return false;
            }

            if (!args.TryGetValue("behavior", out var behavior))
            {
                behavior = "0";
            }

            // Find Solution.xml by walking up from outputBasePath
            var solutionXmlPath = LocateSolutionXml(outputBasePath);
            if (solutionXmlPath == null)
            {
                _logger.LogError("[AddRootComponent] Could not locate Solution.xml by walking up from '{OutputBasePath}'", outputBasePath);
                return false;
            }

            try
            {
                _logger.LogInformation("[AddRootComponent] Adding component (type={Type}) to {Path}", componentType, solutionXmlPath);

                var doc = new XmlDocument { PreserveWhitespace = true };
                doc.Load(solutionXmlPath);

                var rootComponents = doc.SelectSingleNode("//RootComponents");
                if (rootComponents == null)
                {
                    _logger.LogError("[AddRootComponent] Could not find //RootComponents node in {Path}", solutionXmlPath);
                    return false;
                }

                var newComponent = doc.CreateElement("RootComponent");
                newComponent.SetAttribute("type", componentType);

                // Entity-type components use schemaName, form-type components use id
                if (args.TryGetValue("schemaName", out var schemaName))
                {
                    newComponent.SetAttribute("schemaName", schemaName);
                }

                if (args.TryGetValue("id", out var id))
                {
                    newComponent.SetAttribute("id", id);
                }

                newComponent.SetAttribute("behavior", behavior);

                rootComponents.AppendChild(newComponent);

                doc.Save(solutionXmlPath);

                _logger.LogInformation("[AddRootComponent] Successfully added component to Solution.xml");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[AddRootComponent] Failed to modify Solution.xml: {Message}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Locates Solution.xml by walking up from the output path, looking for Other/Solution.xml.
        /// This mirrors the pattern used by the PowerShell scripts which resolve relative to output.
        /// </summary>
        private static string? LocateSolutionXml(string startPath)
        {
            var dir = new DirectoryInfo(startPath);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "Other", "Solution.xml");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
                dir = dir.Parent;
            }
            return null;
        }
    }
}
