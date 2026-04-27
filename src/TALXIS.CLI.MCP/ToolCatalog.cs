#pragma warning disable MCPEXP001

using System.Reflection;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using TALXIS.CLI.Core;

namespace TALXIS.CLI.MCP;

/// <summary>
/// Holds the full internal tool catalog with pre-built schemas and workflow metadata.
/// Used by guide tools for sampling prompts and by execute_operation for dispatch.
/// </summary>
public class ToolCatalog
{
    private readonly Dictionary<string, ToolCatalogEntry> _entries = new();
    private string? _cachedCatalogPrompt;

    /// <summary>
    /// Registers a tool in the catalog with its pre-built schema and metadata.
    /// </summary>
    public void Register(McpToolDescriptor descriptor, JsonElement inputSchema)
    {
        var entry = new ToolCatalogEntry
        {
            Descriptor = descriptor,
            InputSchema = inputSchema,
            Category = DeriveCategory(descriptor.Name),
            Workflow = DeriveWorkflow(descriptor.Name, descriptor.Annotations, descriptor.CliCommandClass)
        };
        _entries[descriptor.Name] = entry;
        _cachedCatalogPrompt = null; // invalidate cache
    }

    /// <summary>
    /// Gets a catalog entry by tool name, or null if not found.
    /// </summary>
    public ToolCatalogEntry? GetEntry(string toolName)
    {
        _entries.TryGetValue(toolName, out var entry);
        return entry;
    }

    /// <summary>
    /// Gets all catalog entries.
    /// </summary>
    public IEnumerable<ToolCatalogEntry> GetAllEntries() => _entries.Values;

    /// <summary>
    /// Gets catalog entries filtered by workflow.
    /// </summary>
    public IEnumerable<ToolCatalogEntry> GetEntriesByWorkflow(string workflow)
    {
        return _entries.Values.Where(e =>
            string.Equals(e.Workflow, workflow, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets full tool details (schema + annotations) for specific tool names.
    /// Returns entries found; silently skips unknown names.
    /// </summary>
    public IEnumerable<ToolCatalogEntry> GetToolDetails(IEnumerable<string> toolNames)
    {
        foreach (var name in toolNames)
        {
            if (_entries.TryGetValue(name, out var entry))
                yield return entry;
        }
    }

    /// <summary>
    /// Builds a formatted catalog string for inclusion in sampling prompts.
    /// Lists all tools with name, description, workflow, and annotation hints.
    /// Cached after first build; invalidated when new tools are registered.
    /// </summary>
    public string GetCatalogPrompt()
    {
        if (_cachedCatalogPrompt is not null)
            return _cachedCatalogPrompt;

        var sb = new StringBuilder();
        sb.AppendLine("# Available Operations");
        sb.AppendLine();

        // Group by workflow for readability
        var grouped = _entries.Values
            .GroupBy(e => e.Workflow)
            .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            sb.AppendLine($"## {group.Key}");
            foreach (var entry in group.OrderBy(e => e.Descriptor.Name))
            {
                var flags = new List<string>();
                if (entry.Descriptor.Annotations?.DestructiveHint == true) flags.Add("DESTRUCTIVE");
                if (entry.Descriptor.Annotations?.ReadOnlyHint == true) flags.Add("read-only");
                if (entry.Descriptor.Annotations?.IdempotentHint == true) flags.Add("idempotent");

                var flagStr = flags.Count > 0 ? $" [{string.Join(", ", flags)}]" : "";
                sb.AppendLine($"- {entry.Descriptor.Name}{flagStr}: {entry.Descriptor.Description}");
            }
            sb.AppendLine();
        }

        _cachedCatalogPrompt = sb.ToString();
        return _cachedCatalogPrompt;
    }

    /// <summary>
    /// Builds a catalog prompt scoped to a specific workflow.
    /// </summary>
    public string GetWorkflowCatalogPrompt(string workflow)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Available Operations — {workflow}");
        sb.AppendLine();

        foreach (var entry in GetEntriesByWorkflow(workflow).OrderBy(e => e.Descriptor.Name))
        {
            var flags = new List<string>();
            if (entry.Descriptor.Annotations?.DestructiveHint == true) flags.Add("DESTRUCTIVE");
            if (entry.Descriptor.Annotations?.ReadOnlyHint == true) flags.Add("read-only");
            if (entry.Descriptor.Annotations?.IdempotentHint == true) flags.Add("idempotent");

            var flagStr = flags.Count > 0 ? $" [{string.Join(", ", flags)}]" : "";
            sb.AppendLine($"- {entry.Descriptor.Name}{flagStr}: {entry.Descriptor.Description}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Total number of tools in the catalog.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Derives a category from the first segment of the tool name.
    /// </summary>
    private static string DeriveCategory(string toolName)
    {
        var firstUnderscore = toolName.IndexOf('_');
        return firstUnderscore > 0
            ? toolName[..firstUnderscore]
            : toolName;
    }

    /// <summary>
    /// Derives a workflow tag from the tool name and annotations.
    /// Maps the tool into one of the standard developer workflows.
    /// </summary>
    private static string DeriveWorkflow(string toolName, ToolAnnotations? annotations, Type? commandClass = null)
    {
        // Explicit attribute takes precedence over name-based heuristic
        if (commandClass is not null)
        {
            var workflowAttr = commandClass.GetCustomAttribute<CliWorkflowAttribute>();
            if (workflowAttr is not null)
                return workflowAttr.Workflow;
        }

        // Workspace tools are always local development
        if (toolName.StartsWith("workspace_", StringComparison.OrdinalIgnoreCase))
            return "local-development";

        // Data commands (not under environment) are local data tooling
        if (toolName.StartsWith("data_", StringComparison.OrdinalIgnoreCase))
            return "local-development";

        // Config tools
        if (toolName.StartsWith("config_", StringComparison.OrdinalIgnoreCase))
            return "configuration";

        // Changeset tools
        if (toolName.StartsWith("environment_changeset_", StringComparison.OrdinalIgnoreCase) ||
            toolName.Contains("changeset", StringComparison.OrdinalIgnoreCase))
            return "changeset-management";

        // Environment data operations (queries, records, bulk)
        if (toolName.StartsWith("environment_data_", StringComparison.OrdinalIgnoreCase))
            return "data-operations";

        // Deployment-related: solution import/export/pack/publish/uninstall, package import/uninstall
        if (IsDeploymentTool(toolName))
            return "deployment";

        // Environment inspection vs mutation: read-only = inspection, others = mutation
        if (toolName.StartsWith("environment_", StringComparison.OrdinalIgnoreCase))
        {
            return annotations?.ReadOnlyHint == true
                ? "environment-inspection"
                : "environment-mutation";
        }

        return "other";
    }

    /// <summary>
    /// Determines if a tool name belongs to the deployment workflow.
    /// </summary>
    private static bool IsDeploymentTool(string toolName)
    {
        var deploymentPatterns = new[]
        {
            "environment_solution_import",
            "environment_solution_export",
            "environment_solution_pack",
            "environment_solution_publish",
            "environment_solution_uninstall",
            "environment_solution_create",
            "environment_solution_delete",
            "environment_solution_list",
            "environment_solution_show",
            "environment_solution_uninstall-check",
            "environment_solution_component_",
            "environment_package_",
            "environment_deployment_",
        };

        return deploymentPatterns.Any(p =>
            toolName.StartsWith(p, StringComparison.OrdinalIgnoreCase) ||
            toolName.Equals(p.TrimEnd('_'), StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// A single entry in the tool catalog, combining the descriptor with pre-built schema and metadata.
/// </summary>
public class ToolCatalogEntry
{
    /// <summary>
    /// The tool descriptor with name, description, command class, annotations.
    /// </summary>
    public required McpToolDescriptor Descriptor { get; init; }

    /// <summary>
    /// Pre-built JSON Schema for the tool's input parameters.
    /// Cached at registration time to avoid rebuilding on every ListTools call.
    /// </summary>
    public required JsonElement InputSchema { get; init; }

    /// <summary>
    /// High-level category derived from the first name segment (e.g., "environment", "workspace", "config").
    /// </summary>
    public required string Category { get; init; }

    /// <summary>
    /// Developer workflow this tool belongs to:
    /// local-development, environment-inspection, environment-mutation,
    /// data-operations, deployment, configuration, changeset-management.
    /// </summary>
    public required string Workflow { get; init; }
}
