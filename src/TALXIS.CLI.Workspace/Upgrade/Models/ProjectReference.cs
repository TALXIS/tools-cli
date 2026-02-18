namespace TALXIS.CLI.Workspace.Upgrade.Models;

public class ProjectReference
{
    public string Include { get; set; } = string.Empty;
    public Dictionary<string, string> Metadata { get; set; } = new();

    public override string ToString() => Include;
}
