namespace TALXIS.CLI.Core.Abstractions;

/// <summary>
/// Marker interface for CLI commands that perform destructive operations
/// (deleting data, uninstalling solutions, etc.). Commands implementing
/// this interface expose a <c>--yes</c> flag; the base class
/// <see cref="TxcLeafCommand"/> checks this interface in
/// <c>RunAsync()</c> and either prompts the user for confirmation or
/// requires <c>--yes</c> in non-interactive environments.
/// <para>
/// This interface works in tandem with
/// <see cref="CliDestructiveAttribute"/>: the attribute drives MCP protocol
/// annotations while this interface drives the interactive CLI confirmation gate.
/// </para>
/// </summary>
public interface IDestructiveCommand
{
    /// <summary>
    /// When <c>true</c>, the user has pre-confirmed the destructive operation
    /// via <c>--yes</c> and no interactive prompt is needed.
    /// </summary>
    bool Yes { get; set; }
}
