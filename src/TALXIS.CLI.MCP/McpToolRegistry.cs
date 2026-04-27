#pragma warning disable MCPEXP001

using TALXIS.CLI.Core;
using ModelContextProtocol.Protocol;

namespace TALXIS.CLI.MCP;

/// <summary>
/// Registry for mapping CLI commands to MCP tool definitions and providing lookup functionality.
/// Populates a <see cref="ToolCatalog"/> with pre-built schemas at startup.
/// </summary>
public class McpToolRegistry
{
    private readonly CliCommandLookupService _commandLookup = new();
    private readonly CliCommandAdapter _schemaBuilder = new();

    /// <summary>
    /// The full internal tool catalog with pre-built schemas and workflow metadata.
    /// Used by guide tools for sampling and by execute_operation for dispatch.
    /// </summary>
    public ToolCatalog Catalog { get; } = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="McpToolRegistry"/> class and registers all CLI tools.
    /// </summary>
    public McpToolRegistry()
    {
        RegisterAllTools();
    }

    /// <summary>
    /// Registers all CLI commands as MCP tools and populates the <see cref="Catalog"/> with pre-built schemas.
    /// Long-running and workflow metadata is derived from attributes on the command classes.
    /// </summary>
    private void RegisterAllTools()
    {
        var rootType = typeof(TxcCliCommand);
        foreach (var descriptor in _commandLookup.EnumerateAllCommands(rootType))
        {
            descriptor.SupportsTaskExecution = Attribute.IsDefined(descriptor.CliCommandClass, typeof(CliLongRunningAttribute));

            // Pre-build and cache the schema in the catalog
            var schema = _schemaBuilder.BuildInputSchema(descriptor.CliCommandClass);
            Catalog.Register(descriptor, schema);
        }

        // Register MCP-specific tools that are not part of the main CLI command tree
        RegisterMcpSpecificTools();
    }

    /// <summary>
    /// Registers MCP-specific tools that should not appear in the main CLI interface
    /// but are available through the MCP protocol.
    /// </summary>
    private void RegisterMcpSpecificTools()
    {
        var descriptor = new McpToolDescriptor
        {
            Name = "copilot-instructions",
            Description = "Creates or updates .github/copilot-instructions.md file with TALXIS CLI instructions in the target project",
            CliCommandClass = typeof(CopilotInstructionsCliCommand),
            SupportsTaskExecution = false
        };

        var schema = _schemaBuilder.BuildInputSchema(descriptor.CliCommandClass);
        Catalog.Register(descriptor, schema);
    }

    /// <summary>
    /// Builds a <see cref="Tool"/> definition for a specific catalog entry.
    /// Used by ActiveToolSet when injecting tools.
    /// </summary>
    public static Tool BuildToolDefinition(ToolCatalogEntry entry)
    {
        var tool = new Tool
        {
            Name = entry.Descriptor.Name,
            Description = entry.Descriptor.Description,
            InputSchema = entry.InputSchema
        };

        if (entry.Descriptor.SupportsTaskExecution)
        {
            tool.Execution = new ToolExecution { TaskSupport = new ToolTaskSupport() };
        }

        if (entry.Descriptor.Annotations is not null)
        {
            tool.Annotations = entry.Descriptor.Annotations;
        }

        return tool;
    }

    /// <summary>
    /// Returns the descriptor for a tool by name, or null if not found.
    /// </summary>
    public McpToolDescriptor? GetDescriptor(string toolName) => Catalog.GetEntry(toolName)?.Descriptor;

    /// <summary>
    /// Finds the command type for a given MCP tool name.
    /// </summary>
    /// <param name="toolName">The name of the tool.</param>
    /// <returns>The <see cref="Type"/> of the command if found; otherwise, null.</returns>
    public Type? FindCommandTypeByToolName(string toolName)
    {
        // First check the catalog (includes MCP-specific tools)
        var entry = Catalog.GetEntry(toolName);
        if (entry is not null)
            return entry.Descriptor.CliCommandClass;

        // Fall back to CLI command hierarchy lookup
        return _commandLookup.FindCommandTypeByToolName(toolName, typeof(TxcCliCommand));
    }
}
