using ModelContextProtocol.Protocol;

namespace TALXIS.CLI.MCP;

/// <summary>
/// Registry for mapping CLI commands to MCP tool definitions and providing lookup functionality.
/// </summary>
public class McpToolRegistry
{
    private readonly McpToolDescriptorProvider _descriptorProvider = new();
    private readonly CliCommandLookupService _commandLookup = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="McpToolRegistry"/> class and registers all CLI tools.
    /// </summary>
    public McpToolRegistry()
    {
        RegisterAllTools();
    }

    /// <summary>
    /// Registers all CLI commands as MCP tools using the metadata provider.
    /// </summary>
    private void RegisterAllTools()
    {
        var rootType = typeof(TxcCliCommand);
        foreach (var descriptor in _commandLookup.EnumerateAllCommands(rootType))
        {
            _descriptorProvider.AddDescriptor(descriptor.Name, descriptor.Description, descriptor.CliCommandClass);
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
        _descriptorProvider.AddDescriptor(
            "copilot-instructions",
            "Creates or updates .github/copilot-instructions.md file with TALXIS CLI instructions in the target project",
            typeof(CopilotInstructionsCliCommand)
        );
    }

    /// <summary>
    /// Lists all registered MCP tools with their metadata and input schemas.
    /// </summary>
    /// <returns>A list of <see cref="Tool"/> definitions.</returns>
    public List<Tool> ListTools()
    {
        var toolDefinitions = new List<Tool>();
        foreach (var descriptor in _descriptorProvider.GetAllDescriptors())
        {
            toolDefinitions.Add(new Tool
            {
                Name = descriptor.Name,
                Description = descriptor.Description,
                InputSchema = new CliCommandAdapter().BuildInputSchema(descriptor.CliCommandClass)
            });
        }
        return toolDefinitions;
    }

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
