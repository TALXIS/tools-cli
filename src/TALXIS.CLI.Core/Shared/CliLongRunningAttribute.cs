namespace TALXIS.CLI.Core;

/// <summary>
/// Marks a CLI command as a long-running operation that supports async task-augmented execution.
/// In MCP mode, clients can request "call-now, fetch-later" execution for these commands.
/// In terminal mode, these commands display progress indicators.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CliLongRunningAttribute : Attribute { }
