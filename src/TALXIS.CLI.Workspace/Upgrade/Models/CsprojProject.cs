using System.Xml.Linq;

namespace TALXIS.CLI.Workspace.Upgrade.Models;

public class CsprojProject
{
    public string FilePath { get; set; } = string.Empty;
    public bool IsSdkStyle { get; set; }
    public string? Sdk { get; set; }

    // Properties
    public Dictionary<string, string> Properties { get; set; } = new();

    // References
    public List<PackageReference> PackageReferences { get; set; } = new();
    public List<ProjectReference> ProjectReferences { get; set; } = new();
    public List<AssemblyReference> AssemblyReferences { get; set; } = new();

    // Custom elements that need to be preserved
    public List<XElement> CustomPropertyGroups { get; set; } = new();
    public List<XElement> CustomItemGroups { get; set; } = new();
    public List<XElement> CustomTargets { get; set; } = new();
    public List<XElement> CustomImports { get; set; } = new();

    // Original XML for reference
    public XDocument? OriginalXml { get; set; }
}
