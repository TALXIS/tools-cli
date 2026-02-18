namespace TALXIS.CLI.Workspace.Upgrade.Models;

public class PackageReference
{
    public string Name { get; set; } = string.Empty;
    public string? Version { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();

    public override string ToString() => $"{Name} {Version}";
}
