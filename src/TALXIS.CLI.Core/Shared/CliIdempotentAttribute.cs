namespace TALXIS.CLI.Core;

/// <summary>
/// Marks a CLI command as idempotent — calling it repeatedly with the same
/// arguments produces the same result with no additional side effects.
/// MCP clients may safely retry idempotent tools on transient failures.
/// <para>
/// This attribute is optional and can be combined with either
/// <see cref="CliReadOnlyAttribute"/> or <see cref="CliDestructiveAttribute"/>.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CliIdempotentAttribute : Attribute { }
