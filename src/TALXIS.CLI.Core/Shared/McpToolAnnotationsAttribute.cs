namespace TALXIS.CLI.Core;

/// <summary>
/// Declares MCP tool annotation hints on a CLI command class. These hints are
/// emitted in the tool's <c>annotations</c> field when the MCP server lists
/// tools, allowing clients to adjust confirmation behaviour (e.g. prompt for
/// human-in-the-loop approval before executing a destructive tool).
/// <para>
/// All properties default to <c>false</c>. Set them explicitly to <c>true</c>
/// to include them in the protocol output.
/// </para>
/// <para>
/// See the MCP specification (2025-06-18) §Tool Annotations and the
/// <see href="https://modelcontextprotocol.io/docs/tutorials/security/security_best_practices">
/// MCP Security Best Practices</see> for guidance on annotation semantics.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class McpToolAnnotationsAttribute : Attribute
{
    /// <summary>
    /// Hint that this tool may perform destructive updates (delete data, drop
    /// resources, modify state irreversibly). Clients SHOULD prompt for
    /// confirmation before invoking tools with <c>DestructiveHint = true</c>.
    /// </summary>
    public bool DestructiveHint { get; set; }

    /// <summary>
    /// Hint that this tool only reads data and has no side effects. Clients
    /// MAY auto-approve read-only tools without human confirmation.
    /// </summary>
    public bool ReadOnlyHint { get; set; }

    /// <summary>
    /// Hint that calling this tool repeatedly with the same arguments produces
    /// the same result and has no additional side effects.
    /// </summary>
    public bool IdempotentHint { get; set; }

    /// <summary>
    /// Hint that this tool interacts with entities outside the local
    /// environment (network calls, remote APIs). When false, the tool
    /// operates only on local state.
    /// </summary>
    public bool OpenWorldHint { get; set; }
}
