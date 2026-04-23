namespace TALXIS.CLI.Core;

/// <summary>
/// Marks a <see cref="DotMake.CommandLine.CliCommandAttribute"/>-decorated class as excluded from the MCP tool registry.
/// The command (and every descendant in its sub-tree) is skipped when the MCP server enumerates tools.
/// <para>
/// Apply to commands that are not meaningful in a headless/MCP agent context:
/// interactive flows, long-running server processes, or local-only maintenance operations.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class McpIgnoreAttribute : Attribute { }
