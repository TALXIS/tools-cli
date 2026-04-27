namespace TALXIS.CLI.Core;

/// <summary>
/// Assigns a CLI command to a developer workflow. Used by the MCP server for
/// guide tool scoping and by the CLI for help grouping.
/// Standard workflows: local-development, environment-inspection, environment-mutation,
/// data-operations, deployment, configuration, changeset-management.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CliWorkflowAttribute : Attribute
{
    public string Workflow { get; }
    public CliWorkflowAttribute(string workflow) => Workflow = workflow;
}
