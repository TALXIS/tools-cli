using System.Xml.Linq;

namespace TALXIS.CLI.Core.Resolution;

/// <summary>A custom connector defined in the workspace by a DevKit <c>Connector</c> project.</summary>
/// <param name="LogicalName">
/// The connector's Dataverse schema name (<c>&lt;prefix&gt;_connectors&lt;name&gt;</c>) when a
/// solution in the workspace includes it; null when none does yet.
/// </param>
/// <param name="ConnectorName">The DevKit <c>ConnectorName</c>: the last dot-segment of the project name unless set explicitly.</param>
/// <param name="ProjectPath">The project file.</param>
/// <param name="DefinitionPath">The connector's Swagger 2.0 definition.</param>
public sealed record WorkspaceConnector(
    string? LogicalName,
    string ConnectorName,
    string ProjectPath,
    string DefinitionPath)
{
    /// <summary>The name answers report: the logical name when known, else the connector name.</summary>
    public string Identifier => LogicalName ?? ConnectorName;

    /// <summary>The project name, e.g. <c>Connectors.OpenFoodFacts</c>.</summary>
    public string ProjectName => Path.GetFileNameWithoutExtension(ProjectPath);
}

/// <summary>
/// Finds the custom connectors a workspace defines, the way the DevKit build
/// does: a project with <c>&lt;ProjectType&gt;Connector&lt;/ProjectType&gt;</c> and its
/// <c>apiDefinition.swagger.json</c>. The logical name is the
/// <c>RootComponent type="372"</c> a solution's <c>Solution.xml</c> includes for it.
/// </summary>
/// <remarks>Reads files only; nothing here touches an environment.</remarks>
public static class WorkspaceConnectorLocator
{
    /// <summary>Solution component type of a custom connector.</summary>
    private const string ConnectorComponentType = "372";

    private const string DefaultDefinitionFile = "apiDefinition.swagger.json";

    /// <summary>
    /// The workspace root for <paramref name="startDirectory"/>: the nearest
    /// directory with <c>.txc/workspace.json</c>, else the nearest Git
    /// repository root, else the nearest directory holding a solution file.
    /// Null outside any workspace.
    /// </summary>
    public static string? FindWorkspaceRoot(string startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory) || !Directory.Exists(startDirectory))
            return null;

        var start = new DirectoryInfo(Path.GetFullPath(startDirectory));
        return Ancestors(start).FirstOrDefault(d => File.Exists(Path.Combine(d.FullName, WorkspaceDiscovery.DirectoryName, WorkspaceDiscovery.FileName)))?.FullName
            ?? Ancestors(start).FirstOrDefault(d => Directory.Exists(Path.Combine(d.FullName, ".git")) || File.Exists(Path.Combine(d.FullName, ".git")))?.FullName
            ?? Ancestors(start).FirstOrDefault(d => d.EnumerateFiles("*.sln").Any() || d.EnumerateFiles("*.slnx").Any())?.FullName;
    }

    /// <summary>Every connector project under <paramref name="workspaceRoot"/> that has a JSON definition.</summary>
    public static IReadOnlyList<WorkspaceConnector> FindAll(string workspaceRoot)
    {
        var schemaNames = ReadConnectorSchemaNames(workspaceRoot);
        var connectors = new List<WorkspaceConnector>();

        foreach (var project in WorkspaceFiles.Enumerate(workspaceRoot, "*.csproj"))
        {
            var properties = ReadProperties(project);
            if (properties is null
                || !properties.TryGetValue("ProjectType", out var type)
                || !string.Equals(type, "Connector", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var directory = Path.GetDirectoryName(project)!;
            var name = Literal(properties, "ConnectorName")
                ?? Path.GetFileNameWithoutExtension(project).Split('.')[^1];
            var definition = Literal(properties, "ConnectorApiDefinitionPath") is { } custom
                ? Path.GetFullPath(Path.Combine(directory, custom))
                : Path.Combine(directory, DefaultDefinitionFile);
            if (!File.Exists(definition))
                continue;

            var suffix = "_connectors" + name.ToLowerInvariant();
            var logicalName = schemaNames.FirstOrDefault(s => s.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                && s.IndexOf('_', StringComparison.Ordinal) == s.Length - suffix.Length);

            connectors.Add(new WorkspaceConnector(logicalName, name, project, definition));
        }

        return connectors;
    }

    /// <summary>
    /// The workspace connector <paramref name="connector"/> names, by logical
    /// name, connector name or project name; null when none matches.
    /// </summary>
    public static WorkspaceConnector? Find(string workspaceRoot, string connector)
    {
        if (string.IsNullOrWhiteSpace(connector))
            return null;

        var name = connector.Trim();
        return FindAll(workspaceRoot).FirstOrDefault(c =>
            string.Equals(c.LogicalName, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(c.ConnectorName, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(c.ProjectName, name, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<DirectoryInfo> Ancestors(DirectoryInfo start)
    {
        for (var dir = start; dir is not null; dir = dir.Parent)
            yield return dir;
    }

    private static HashSet<string> ReadConnectorSchemaNames(string workspaceRoot)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in WorkspaceFiles.Enumerate(workspaceRoot, "Solution.xml"))
        {
            try
            {
                foreach (var component in XDocument.Load(file).Descendants("RootComponent"))
                {
                    if ((string?)component.Attribute("type") == ConnectorComponentType
                        && (string?)component.Attribute("schemaName") is { Length: > 0 } schemaName)
                    {
                        names.Add(schemaName);
                    }
                }
            }
            catch (System.Xml.XmlException)
            {
                // A malformed Solution.xml fails the build with a better message than this could give.
            }
        }

        return names;
    }

    private static Dictionary<string, string>? ReadProperties(string projectFile)
    {
        try
        {
            var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var element in XDocument.Load(projectFile).Descendants().Where(e => e.Parent?.Name.LocalName == "PropertyGroup"))
                properties[element.Name.LocalName] = element.Value.Trim();
            return properties;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>A property value usable as is; one built from other properties (<c>$(...)</c>) is not evaluated here.</summary>
    private static string? Literal(Dictionary<string, string> properties, string name)
        => properties.TryGetValue(name, out var value) && value.Length > 0 && !value.Contains("$(", StringComparison.Ordinal)
            ? value
            : null;
}
