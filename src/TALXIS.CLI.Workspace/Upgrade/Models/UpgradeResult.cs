namespace TALXIS.CLI.Workspace.Upgrade.Models;

public class UpgradeResult
{
    public bool Success { get; set; }
    public string? OutputFilePath { get; set; }
    public string? BackupPath { get; set; }
    public bool BackupCreated { get; set; }
    public int PackageReferencesFound { get; set; }
    public int ProjectReferencesFound { get; set; }
    public int AssemblyReferencesFound { get; set; }
    public int CustomPropertiesFound { get; set; }
    public string? ErrorMessage { get; set; }
    public Exception? Exception { get; set; }
}
