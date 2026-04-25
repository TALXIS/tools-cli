namespace TALXIS.CLI.Core;

/// <summary>
/// Marks a CLI command as read-only — it only queries or displays data and has
/// no side effects. MCP clients may auto-approve read-only tools without
/// human-in-the-loop confirmation.
/// <para>
/// Mutually exclusive with <see cref="CliDestructiveAttribute"/>. Every leaf
/// command must carry exactly one of the two — enforced at build time by TXC004.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CliReadOnlyAttribute : Attribute { }
