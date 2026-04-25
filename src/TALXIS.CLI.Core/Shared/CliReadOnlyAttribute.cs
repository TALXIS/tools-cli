namespace TALXIS.CLI.Core;

/// <summary>
/// Marks a CLI command as read-only — it only queries or displays data and has
/// no side effects. MCP clients may auto-approve read-only tools without
/// human-in-the-loop confirmation.
/// <para>
/// Mutually exclusive with <see cref="CliDestructiveAttribute"/>. Every leaf
/// command must declare a safety posture — enforced at build time by TXC004.
/// The requirement may be satisfied by <see cref="CliReadOnlyAttribute"/>,
/// <see cref="CliDestructiveAttribute"/>, or <see cref="CliIdempotentAttribute"/>.
/// <see cref="CliIdempotentAttribute"/> may be combined with either.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CliReadOnlyAttribute : Attribute { }
