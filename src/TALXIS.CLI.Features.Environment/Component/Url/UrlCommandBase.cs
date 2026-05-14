using System.Text.RegularExpressions;
using System.Xml.Linq;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;

namespace TALXIS.CLI.Features.Environment.Component.Url;

/// <summary>
/// Shared base for <see cref="UrlGetCliCommand"/> and <see cref="UrlOpenCliCommand"/>.
/// Contains the common CLI options and URL building logic.
/// </summary>
[CliReadOnly]
public abstract class UrlCommandBase : ProfiledCliCommand
{
    [CliOption(Name = "--type", Description = "Component type — accepts canonical name (Entity, Workflow), alias (Table, Flow), template name (pp-entity), or integer type code.", Required = false)]
    public string? Type { get; set; }

    [CliOption(Name = "--param", Description = "URL parameter in key=value format. Can be specified multiple times. Available parameters depend on the component type.", Required = false)]
    public List<string> Param { get; set; } = new();

    [CliOption(Name = "--file", Description = "Path to a local component file. Auto-detects component type and GUID from the file path and metadata.", Required = false)]
    public string? File { get; set; }

    /// <summary>
    /// Regex for extracting a GUID from a file name (e.g. {xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}).
    /// </summary>
    private static readonly Regex GuidInFileNamePattern = new(
        @"\{?([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\}?",
        RegexOptions.Compiled);

    /// <summary>
    /// Builds the URL from the shared options. Returns null on failure (error already logged).
    /// </summary>
    protected async Task<UrlBuilderResult?> BuildUrlFromOptionsAsync()
    {
        // When --file is provided, resolve type and id from the file before the normal flow.
        if (!string.IsNullOrWhiteSpace(File))
        {
            var resolved = ResolveFromFile(Path.GetFullPath(File));
            if (resolved is null)
                return null;
            Type ??= resolved.Value.Type;
            // Inject the resolved id into --param unless already provided
            var existingParams = UrlBuilder.ParseParams(Param);
            if (!existingParams.ContainsKey("id"))
                Param.Add($"id={resolved.Value.Id}");
        }

        if (string.IsNullOrWhiteSpace(Type))
        {
            Logger.LogError("'--type' is required (or use '--file' to auto-detect).");
            return null;
        }

        var parameters = UrlBuilder.ParseParams(Param);
        return await UrlBuilder.BuildUrlAsync(Type, parameters, Profile, Logger).ConfigureAwait(false);
    }

    /// <summary>
    /// Auto-detects the component type and GUID from a local file path and its metadata.
    /// </summary>
    private (string Type, string Id)? ResolveFromFile(string absolutePath)
    {
        var normalizedPath = absolutePath.Replace('\\', '/');
        var segments = normalizedPath.Split('/');

        string? componentType = null;
        string? componentId = null;

        // Detect component type from path segments
        for (int i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];

            if (segment.Equals("Workflows", StringComparison.OrdinalIgnoreCase) && i + 1 < segments.Length)
            {
                componentType = "Workflow";
                // Read GUID from companion .data.xml file
                var dataXmlPath = absolutePath + ".data.xml";
                if (System.IO.File.Exists(dataXmlPath))
                {
                    componentId = ExtractWorkflowIdFromDataXml(dataXmlPath);
                }
                break;
            }

            if (segment.Equals("Entities", StringComparison.OrdinalIgnoreCase) && i + 1 < segments.Length)
            {
                // Determine sub-type based on further path segments
                if (i + 2 < segments.Length && segments[i + 2].Equals("FormXml", StringComparison.OrdinalIgnoreCase))
                {
                    componentType = "SystemForm";
                    componentId = ExtractGuidFromPathOrContent(absolutePath, segments);
                }
                else if (i + 2 < segments.Length && segments[i + 2].Equals("SavedQueries", StringComparison.OrdinalIgnoreCase))
                {
                    componentType = "SavedQuery";
                    componentId = ExtractGuidFromPathOrContent(absolutePath, segments);
                }
                else if (i + 1 < segments.Length && segments.Length > i + 2 && segments[i + 2].Equals("Entity.xml", StringComparison.OrdinalIgnoreCase))
                {
                    componentType = "Entity";
                    componentId = ExtractGuidFromXmlContent(absolutePath, "EntityId", "MetadataId");
                }
                else
                {
                    // Generic entity path — check if it's Entity.xml directly
                    var fileName = Path.GetFileName(absolutePath);
                    if (fileName.Equals("Entity.xml", StringComparison.OrdinalIgnoreCase))
                    {
                        componentType = "Entity";
                        componentId = ExtractGuidFromXmlContent(absolutePath, "EntityId", "MetadataId");
                    }
                }
                break;
            }

            if (segment.Equals("Roles", StringComparison.OrdinalIgnoreCase))
            {
                componentType = "Role";
                componentId = ExtractGuidFromFileName(absolutePath);
                break;
            }

            if (segment.Equals("WebResources", StringComparison.OrdinalIgnoreCase))
            {
                componentType = "WebResource";
                componentId = ExtractGuidFromPathOrContent(absolutePath, segments);
                break;
            }

            if (segment.Equals("AppModules", StringComparison.OrdinalIgnoreCase))
            {
                componentType = "AppModule";
                componentId = ExtractGuidFromPathOrContent(absolutePath, segments);
                break;
            }

            if (segment.Equals("OptionSets", StringComparison.OrdinalIgnoreCase))
            {
                componentType = "OptionSet";
                componentId = ExtractGuidFromPathOrContent(absolutePath, segments);
                break;
            }
        }

        if (componentType is null)
        {
            Logger.LogError("Could not determine component type from file path: {Path}.", absolutePath);
            return null;
        }

        if (componentId is null)
        {
            Logger.LogError("Could not extract component GUID from file: {Path}.", absolutePath);
            return null;
        }

        Logger.LogInformation("Resolved --file to type '{Type}' with id '{Id}'.", componentType, componentId);
        return (componentType, componentId);
    }

    /// <summary>
    /// Reads the WorkflowId from a companion .data.xml file.
    /// Expected format: <c>&lt;Workflow WorkflowId="{guid}" ...&gt;</c>
    /// </summary>
    private static string? ExtractWorkflowIdFromDataXml(string dataXmlPath)
    {
        var doc = XDocument.Load(dataXmlPath);
        var workflowId = doc.Root?.Attribute("WorkflowId")?.Value
            ?? doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Workflow")?.Attribute("WorkflowId")?.Value;
        if (workflowId is not null)
            return workflowId.Trim('{', '}');
        return null;
    }

    /// <summary>
    /// Extracts a GUID from the file name using regex.
    /// </summary>
    private static string? ExtractGuidFromFileName(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var match = GuidInFileNamePattern.Match(fileName);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Tries to extract a GUID from the file name first, then falls back to XML content.
    /// </summary>
    private static string? ExtractGuidFromPathOrContent(string filePath, string[] segments)
    {
        // Try file name first
        var fromName = ExtractGuidFromFileName(filePath);
        if (fromName is not null)
            return fromName;

        // Try XML content with common attribute patterns
        if (filePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(filePath))
            return ExtractGuidFromXmlContent(filePath, "id", "formid", "savedqueryid", "webresourceid");

        return null;
    }

    /// <summary>
    /// Searches XML content for a GUID in common id-bearing attributes or elements.
    /// </summary>
    private static string? ExtractGuidFromXmlContent(string xmlPath, params string[] attributeNames)
    {
        if (!System.IO.File.Exists(xmlPath))
            return null;
        var doc = XDocument.Load(xmlPath);
        // Check root element attributes
        if (doc.Root is not null)
        {
            foreach (var attrName in attributeNames)
            {
                var attr = doc.Root.Attributes()
                    .FirstOrDefault(a => a.Name.LocalName.Equals(attrName, StringComparison.OrdinalIgnoreCase));
                if (attr is not null && Guid.TryParse(attr.Value.Trim('{', '}'), out _))
                    return attr.Value.Trim('{', '}');
            }
            // Check child elements with those names
            foreach (var attrName in attributeNames)
            {
                var elem = doc.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName.Equals(attrName, StringComparison.OrdinalIgnoreCase));
                if (elem is not null && Guid.TryParse(elem.Value.Trim('{', '}'), out _))
                    return elem.Value.Trim('{', '}');
            }
        }
        // Last resort: search entire content for a GUID
        var content = System.IO.File.ReadAllText(xmlPath);
        var match = GuidInFileNamePattern.Match(content);
        return match.Success ? match.Groups[1].Value : null;
    }
}
