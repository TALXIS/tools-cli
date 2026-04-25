namespace TALXIS.CLI.Core;

/// <summary>
/// Marks a CLI command as destructive — it may delete data, uninstall components,
/// or irreversibly modify state. The required <paramref name="impact"/> message
/// describes the consequences and is surfaced to both interactive CLI users
/// (in the confirmation prompt) and MCP agent clients (in the tool description).
/// <para>
/// Commands carrying this attribute must also implement
/// <see cref="Abstractions.IDestructiveCommand"/> to expose the <c>--yes</c> flag.
/// The base class <see cref="TxcLeafCommand"/> enforces confirmation automatically.
/// </para>
/// </summary>
/// <param name="impact">
/// Human-readable description of the destructive impact, e.g.
/// "Permanently removes the solution and all its components from the target environment."
/// Must not be null or whitespace — enforced at build time by the TXC004 analyzer.
/// </param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CliDestructiveAttribute(string impact) : Attribute
{
    /// <summary>
    /// Human-readable description of the destructive impact of this command.
    /// </summary>
    public string Impact { get; } = impact;
}
