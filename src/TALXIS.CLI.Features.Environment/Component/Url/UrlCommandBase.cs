using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.Platform.Metadata;
using TALXIS.Platform.Metadata.Serialization.Xml;

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

    [CliOption(Name = "--file", Description = "Path to a local component file. Auto-detects component type and GUID by loading the workspace with the platform metadata library.", Required = false)]
    public string? File { get; set; }

    /// <summary>
    /// Builds the URL from the shared options. Returns null on failure (error already logged).
    /// </summary>
    protected async Task<UrlBuilderResult?> BuildUrlFromOptionsAsync()
    {
        // When --file is provided, resolve type and params from the workspace metadata.
        if (!string.IsNullOrWhiteSpace(File))
        {
            var resolved = ResolveFromFile(Path.GetFullPath(File));
            if (resolved is null)
                return null;
            Type ??= resolved.Value.Type;
            // Inject resolved params unless the user already specified them
            var existingParams = UrlBuilder.ParseParams(Param);
            foreach (var kvp in resolved.Value.Params)
            {
                if (!existingParams.ContainsKey(kvp.Key))
                    Param.Add($"{kvp.Key}={kvp.Value}");
            }
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
    /// Auto-detects the component type and URL parameters from a local file path by loading
    /// the workspace with <see cref="XmlWorkspaceReader"/> and matching the file to a component.
    /// </summary>
    /// <remarks>
    /// The SourceDocumentKey carries structured identifiers per type:
    /// <list type="bullet">
    ///   <item><c>Entity:logicalname</c> — ObjectId is the entity logical name</item>
    ///   <item><c>Form:entityname:{guid}</c> — ObjectId is the form GUID</item>
    ///   <item><c>View:entityname:{guid}</c> — ObjectId is the view GUID</item>
    ///   <item><c>Workflow:name</c> — ObjectId is the workflow GUID</item>
    /// </list>
    /// </remarks>
    private (string Type, Dictionary<string, string> Params)? ResolveFromFile(string absolutePath)
    {
        var workspaceRoot = FindWorkspaceRoot(absolutePath);
        if (workspaceRoot is null)
        {
            Logger.LogError("Could not find workspace root (Other/Solution.xml) for file: {Path}.", absolutePath);
            return null;
        }

        var reader = new XmlWorkspaceReader();
        var workspace = reader.Load(workspaceRoot);

        var normalizedFile = Path.GetFullPath(absolutePath);

        foreach (var component in workspace.EnumerateLayerComponents())
        {
            if (!IsFileMatch(component, normalizedFile, workspaceRoot))
                continue;

            var typeName = component.Type.ToString();
            var resolvedParams = ExtractUrlParams(component);

            Logger.LogInformation("Resolved --file to type '{Type}' with params: {Params}.",
                typeName, string.Join(", ", resolvedParams.Select(p => $"{p.Key}={p.Value}")));

            return (typeName, resolvedParams);
        }

        Logger.LogError("Could not match file to any component in workspace: {Path}.", absolutePath);
        return null;
    }

    /// <summary>
    /// Walks up from the file to find the solution workspace root (directory containing Other/Solution.xml).
    /// Also checks for .cdsproj/.csproj files that declare a SolutionRootPath property.
    /// </summary>
    private static string? FindWorkspaceRoot(string absolutePath)
    {
        var dir = Path.GetDirectoryName(absolutePath);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir, "Other", "Solution.xml")))
                return dir;

            var csproj = Directory.GetFiles(dir, "*.cdsproj").FirstOrDefault()
                      ?? Directory.GetFiles(dir, "*.csproj").FirstOrDefault();
            if (csproj is not null)
            {
                var projDoc = System.Xml.Linq.XDocument.Load(csproj);
                var ns = projDoc.Root?.Name.Namespace ?? System.Xml.Linq.XNamespace.None;
                var solutionRootPath = projDoc.Descendants(ns + "SolutionRootPath").FirstOrDefault()?.Value ?? ".";
                var candidateRoot = Path.GetFullPath(Path.Combine(dir, solutionRootPath));
                if (System.IO.File.Exists(Path.Combine(candidateRoot, "Other", "Solution.xml")))
                    return candidateRoot;
            }
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    /// <summary>
    /// Checks whether the given component matches the target file path.
    /// Uses the metadata source file path for an exact match.
    /// </summary>
    private static bool IsFileMatch(
        TALXIS.Platform.Metadata.Solutions.LayerComponentDescriptor component,
        string normalizedFile,
        string workspaceRoot)
    {
        if (component.Metadata?.Source?.FilePath is null)
            return false;

        // Source.FilePath is absolute from the workspace reader
        var componentFile = Path.GetFullPath(component.Metadata.Source.FilePath);
        return string.Equals(componentFile, normalizedFile, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extracts URL parameters from a matched component based on its type and SourceDocumentKey.
    /// Different types produce different parameter sets for the URL builder.
    /// </summary>
    private static Dictionary<string, string> ExtractUrlParams(
        TALXIS.Platform.Metadata.Solutions.LayerComponentDescriptor component)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // SourceDocumentKey format: "Type:entityname:{guid}" or "Type:name"
        var keyParts = component.SourceDocumentKey?.Split(':') ?? Array.Empty<string>();

        switch (component.Type)
        {
            case ComponentType.Entity:
                // ObjectId is the entity logical name
                result["entity"] = component.ObjectId;
                break;

            case ComponentType.SystemForm:
                // SourceDocumentKey: "Form:entityname:{guid}"
                result["id"] = component.ObjectId;
                if (keyParts.Length >= 2)
                    result["entity"] = keyParts[1];
                break;

            case ComponentType.SavedQuery:
                // SourceDocumentKey: "View:entityname:{guid}"
                result["id"] = component.ObjectId;
                if (keyParts.Length >= 2)
                    result["entity"] = keyParts[1];
                break;

            default:
                // For most types (Workflow, Role, AppModule, etc.), ObjectId is the GUID
                result["id"] = component.ObjectId;
                break;
        }

        return result;
    }
}
