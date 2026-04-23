namespace TALXIS.CLI.Core.Model;

/// <summary>
/// Root of <c>&lt;repo&gt;/.txc/workspace.json</c>. v1 carries only a default
/// profile pointer; see plan.md §Storage layout.
/// </summary>
public sealed class WorkspaceConfig
{
    public string? DefaultProfile { get; set; }
}
