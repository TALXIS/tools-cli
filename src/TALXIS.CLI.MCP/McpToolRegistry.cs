using System.Reflection;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using TALXIS.CLI;
using TALXIS.CLI.MCP;

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
        return _commandLookup.FindCommandTypeByToolName(toolName, typeof(TxcCliCommand));
    }
}
