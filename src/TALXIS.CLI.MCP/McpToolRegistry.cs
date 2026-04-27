#pragma warning disable MCPEXP001

using ModelContextProtocol.Protocol;

namespace TALXIS.CLI.MCP;

/// <summary>
/// Registry for mapping CLI commands to MCP tool definitions and providing lookup functionality.
/// Populates a <see cref="ToolCatalog"/> with pre-built schemas at startup.
/// </summary>
public class McpToolRegistry
{
    private readonly McpToolDescriptorProvider _descriptorProvider = new();
    private readonly CliCommandLookupService _commandLookup = new();
    private readonly CliCommandAdapter _schemaBuilder = new();

    /// <summary>
    /// The full internal tool catalog with pre-built schemas and workflow metadata.
    /// Used by guide tools for sampling and by execute_operation for dispatch.
    /// </summary>
    public ToolCatalog Catalog { get; } = new();

    /// <summary>
    /// CLI command types that represent long-running operations and support task-augmented execution.
    /// </summary>
    private static readonly HashSet<Type> _longRunningCommandTypes = new()
    {
        typeof(TALXIS.CLI.Features.Data.DataPackageImportCliCommand),
        typeof(TALXIS.CLI.Features.Environment.Package.PackageImportCliCommand),
        typeof(TALXIS.CLI.Features.Environment.Package.PackageUninstallCliCommand),
        typeof(TALXIS.CLI.Features.Environment.Solution.SolutionImportCliCommand),
        typeof(TALXIS.CLI.Features.Environment.Solution.SolutionUninstallCliCommand),
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="McpToolRegistry"/> class and registers all CLI tools.
    /// </summary>
    public McpToolRegistry()
    {
        RegisterAllTools();
    }

    /// <summary>
    /// Registers all CLI commands as MCP tools using the metadata provider,
    /// and populates the <see cref="Catalog"/> with pre-built schemas.
    /// </summary>
    private void RegisterAllTools()
    {
        var rootType = typeof(TxcCliCommand);
        foreach (var descriptor in _commandLookup.EnumerateAllCommands(rootType))
        {
            descriptor.SupportsTaskExecution = _longRunningCommandTypes.Contains(descriptor.CliCommandClass);
            _descriptorProvider.AddDescriptor(descriptor);

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
        _descriptorProvider.AddDescriptor(descriptor);

        var schema = _schemaBuilder.BuildInputSchema(descriptor.CliCommandClass);
        Catalog.Register(descriptor, schema);
    }

    /// <summary>
    /// Lists all registered MCP tools with their metadata and pre-cached input schemas.
    /// </summary>
    /// <returns>A list of <see cref="Tool"/> definitions.</returns>
    public List<Tool> ListTools()
    {
        var toolDefinitions = new List<Tool>();
        foreach (var entry in Catalog.GetAllEntries())
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

            toolDefinitions.Add(tool);
        }
        return toolDefinitions;
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
    public McpToolDescriptor? GetDescriptor(string toolName) => _descriptorProvider.GetDescriptor(toolName);

    /// <summary>
    /// Finds the command type for a given MCP tool name.
    /// </summary>
    /// <param name="toolName">The name of the tool.</param>
    /// <returns>The <see cref="Type"/> of the command if found; otherwise, null.</returns>
    public Type? FindCommandTypeByToolName(string toolName)
    {
        // First check if it's a registered MCP-specific tool
        var descriptor = _descriptorProvider.GetDescriptor(toolName);
        if (descriptor != null)
            return descriptor.CliCommandClass;

        // Fall back to CLI command hierarchy lookup
        return _commandLookup.FindCommandTypeByToolName(toolName, typeof(TxcCliCommand));
    }
}
