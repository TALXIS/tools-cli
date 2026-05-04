using System.Xml;
using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.Abstractions;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Workspace.TemplateEngine;

/// <summary>
/// Post-action processor that replaces the placeholder value inside
/// <c>&lt;CustomizationOptionValuePrefix&gt;</c> in Solution.xml with a
/// deterministic hash derived from the publisher's customization prefix.
/// </summary>
public class ReplaceOptionValuePrefixProcessor : IPostActionProcessor
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(ReplaceOptionValuePrefixProcessor));
    public static Guid ActionProcessorId => new("A1B2C3D4-1003-4000-8000-000000000003");

    public Guid ActionId => ActionProcessorId;

    public bool Process(IEngineEnvironmentSettings environment, IPostAction action)
    {
        return ProcessInternal(environment, action, null!, null!, Environment.CurrentDirectory);
    }

    public bool ProcessInternal(
        IEngineEnvironmentSettings environment,
        IPostAction action,
        ICreationEffects creationEffects,
        ICreationResult? templateCreationResult,
        string outputBasePath)
    {
        var solutionXmlPath = LocateSolutionXml(outputBasePath);
        if (solutionXmlPath == null)
        {
            _logger.LogError(
                "[ReplaceOptionValuePrefix] Could not locate Solution.xml by walking up from '{OutputBasePath}'",
                outputBasePath);
            return false;
        }

        try
        {
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.Load(solutionXmlPath);

            // Read the customization prefix directly from Solution.xml
            // (the template engine already substituted this value in the output file)
            var prefixNode = doc.SelectSingleNode("//CustomizationPrefix");
            if (prefixNode == null || string.IsNullOrWhiteSpace(prefixNode.InnerText))
            {
                _logger.LogError(
                    "[ReplaceOptionValuePrefix] Could not find <CustomizationPrefix> in {Path}",
                    solutionXmlPath);
                return false;
            }

            var customizationPrefix = prefixNode.InnerText.Trim();
            var computedValue = PublisherPrefixHasher.ComputeOptionValuePrefix(customizationPrefix);

            _logger.LogInformation(
                "[ReplaceOptionValuePrefix] Computed option value prefix {Value} from '{Prefix}' in {Path}",
                computedValue, customizationPrefix, solutionXmlPath);

            var node = doc.SelectSingleNode("//CustomizationOptionValuePrefix");
            if (node == null)
            {
                _logger.LogError(
                    "[ReplaceOptionValuePrefix] Could not find <CustomizationOptionValuePrefix> in {Path}",
                    solutionXmlPath);
                return false;
            }

            node.InnerText = computedValue.ToString();
            doc.Save(solutionXmlPath);

            _logger.LogInformation("[ReplaceOptionValuePrefix] Successfully updated Solution.xml");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("[ReplaceOptionValuePrefix] Failed to modify Solution.xml: {Message}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Locates Solution.xml by walking up from the given path, looking for Other/Solution.xml.
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
