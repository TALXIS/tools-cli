namespace TALXIS.CLI.MCP
{
    /// <summary>
    /// Represents a descriptor for a MCP tool, including its name, description and implementing type.
    /// </summary>
    public class McpToolDescriptor
    {
        /// <summary>
        /// The unique name of the MCP tool.
        /// </summary>
        public required string Name { get; set; }

        /// <summary>
        /// A human-readable description of the MCP tool.
        /// </summary>
        public required string Description { get; set; }

        /// <summary>
        /// The <see cref="Type"/> that implements the CLI command.
        /// </summary>
        public required Type CliCommandClass { get; set; }

        /// <summary>
        /// Whether this tool supports task-augmented execution for long-running operations.
        /// When true, clients can request async "call-now, fetch-later" execution.
        /// </summary>
        public bool SupportsTaskExecution { get; set; }
    }
}
