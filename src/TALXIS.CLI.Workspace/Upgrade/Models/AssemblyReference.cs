namespace TALXIS.CLI.Workspace.Upgrade.Models;

public class AssemblyReference
{
    public string Include { get; set; } = string.Empty;
    public string? HintPath { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();

    public override string ToString() => Include;
}
