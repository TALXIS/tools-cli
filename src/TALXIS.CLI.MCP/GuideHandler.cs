#pragma warning disable MCPEXP001

using System.Text;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace TALXIS.CLI.MCP;

/// <summary>
/// Handles guide tool calls. Uses sampling (when available) or keyword matching
/// to discover relevant tools from the catalog and return structured guidance.
/// Also injects discovered tools into the ActiveToolSet for direct calling on subsequent turns.
/// </summary>
public class GuideHandler
{
    private readonly ToolCatalog _catalog;
    private readonly ActiveToolSet _activeToolSet;
    private readonly GuideReasoningEngine? _reasoningEngine;

    public GuideHandler(ToolCatalog catalog, ActiveToolSet activeToolSet, GuideReasoningEngine? reasoningEngine = null)
    {
        _catalog = catalog;
        _activeToolSet = activeToolSet;
        _reasoningEngine = reasoningEngine;
    }

    /// <summary>
    /// Handles a guide call — discovers tools matching the query, returns structured guidance,
    /// and injects matched tools via listChanged.
    /// </summary>
    /// <param name="query">Natural language description of what the user wants to do.</param>
    /// <param name="workflow">Optional explicit workflow filter.</param>
    /// <param name="top">Maximum number of tools to return.</param>
    /// <param name="server">MCP server instance for sampling and listChanged notifications.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A CallToolResult with structured guidance text and a flag indicating if tools were injected.</returns>
    public async Task<(CallToolResult Result, bool ToolsInjected)> HandleAsync(
        string query, string? workflow, int top, McpServer server, CancellationToken ct)
    {
        IEnumerable<ToolCatalogEntry> matchedEntries;

        if (!string.IsNullOrEmpty(workflow))
        {
            // Explicit workflow — return all tools for that workflow
            matchedEntries = _catalog.GetEntriesByWorkflow(workflow);
        }
        else
        {
            // Try sampling first, fall back to keyword matching
            matchedEntries = await DiscoverToolsAsync(query, top, server, ct, "guide");
        }

        var entries = matchedEntries.ToList();
        if (entries.Count == 0)
        {
            return (new CallToolResult
            {
                Content = [new TextContentBlock { Text = $"No matching tools found for: {query}\n\nAvailable workflows: local-development, environment-inspection, environment-mutation, data-operations, deployment, configuration, changeset-management\n\nTry calling with a workflow parameter to see all tools in a domain." }]
            }, false);
        }

        // Inject matched tools into ActiveToolSet for direct calling next turn
        var toolDefinitions = entries.Select(McpToolRegistry.BuildToolDefinition).ToList();
        bool injected = _activeToolSet.InjectTools(toolDefinitions);

        // Build structured response
        var response = BuildGuidanceResponse(entries, query);

        return (new CallToolResult
        {
            Content = [new TextContentBlock { Text = response }]
        }, injected);
    }

    /// <summary>
    /// Handles a domain-specific guide call. Scopes to a specific workflow's catalog.
    /// </summary>
    public async Task<(CallToolResult Result, bool ToolsInjected)> HandleWorkflowGuideAsync(
        string workflowScope, string query, int top, McpServer server, CancellationToken ct, string guideName = "guide")
    {
        IEnumerable<ToolCatalogEntry> matchedEntries;

        if (string.IsNullOrEmpty(query))
        {
            // No query — return all tools for the workflow
            matchedEntries = _catalog.GetEntriesByWorkflow(workflowScope);
        }
        else
        {
            // Use sampling with scoped catalog
            matchedEntries = await DiscoverToolsWithScopedCatalogAsync(
                query, top, workflowScope, server, ct, guideName);
        }

        var entries = matchedEntries.ToList();

        var toolDefinitions = entries.Select(McpToolRegistry.BuildToolDefinition).ToList();
        bool injected = _activeToolSet.InjectTools(toolDefinitions);

        var response = BuildGuidanceResponse(entries, query);

        return (new CallToolResult
        {
            Content = [new TextContentBlock { Text = response }]
        }, injected);
    }

    /// <summary>
    /// Discovers tools matching the query. Uses sampling (if client supports it)
    /// for high-quality LLM-based tool selection, with keyword matching as fallback.
    /// </summary>
    /// <param name="guideName">The guide tool name, used to load relevant internal skills.</param>
    private async Task<IEnumerable<ToolCatalogEntry>> DiscoverToolsAsync(
        string query, int top, McpServer server, CancellationToken ct, string guideName = "guide")
    {
        try
        {
            var skillsContext = _reasoningEngine?.GetSkillsContext(guideName) ?? "";
            var samplingResult = await SampleToolSelectionAsync(query, _catalog.GetCatalogPrompt(), skillsContext, top, server, ct);
            if (samplingResult is not null && samplingResult.Count > 0)
                return samplingResult;
        }
        catch
        {
            // Sampling not supported or failed — fall through to keyword matching
        }

        return KeywordMatch(query, _catalog.GetAllEntries(), top);
    }

    /// <summary>
    /// Discovers tools using a workflow-scoped catalog.
    /// </summary>
    private async Task<IEnumerable<ToolCatalogEntry>> DiscoverToolsWithScopedCatalogAsync(
        string query, int top, string workflow, McpServer server, CancellationToken ct, string guideName = "guide")
    {
        var scopedEntries = _catalog.GetEntriesByWorkflow(workflow).ToList();

        try
        {
            var skillsContext = _reasoningEngine?.GetSkillsContext(guideName) ?? "";
            var catalogPrompt = _catalog.GetWorkflowCatalogPrompt(workflow);
            var samplingResult = await SampleToolSelectionAsync(query, catalogPrompt, skillsContext, top, server, ct);
            if (samplingResult is not null && samplingResult.Count > 0)
                return samplingResult;
        }
        catch
        {
            // Sampling not supported or failed
        }

        return KeywordMatch(query, scopedEntries, top);
    }

    /// <summary>
    /// Sends a sampling/createMessage request to the client's LLM for tool selection.
    /// The client's own LLM understands the user's intent and selects the most relevant tools.
    /// Internal skills are injected into the system prompt for proprietary reasoning context.
    /// </summary>
    private async Task<List<ToolCatalogEntry>?> SampleToolSelectionAsync(
        string query, string catalogPrompt, string internalSkillsContext, int top, McpServer server, CancellationToken ct)
    {
        var systemPrompt = $@"You are a tool selection assistant. Given the user's task description and a catalog of available operations, select the {top} most relevant tools.

IMPORTANT: Prefer LOCAL workspace operations over LIVE environment operations when the task can be done locally. Environment operations take 30 seconds to 5 minutes and should only be used for inspection, troubleshooting, or deployment after local validation.

Return ONLY a JSON array of tool names, nothing else. Example: [""workspace_component_create"", ""workspace_component_type_list""]

{catalogPrompt}{internalSkillsContext}";

        var samplingParams = new CreateMessageRequestParams
        {
            Messages =
            [
                new SamplingMessage
                {
                    Role = Role.User,
                    Content = [new TextContentBlock { Text = $"Select the most relevant tools for this task: {query}" }]
                }
            ],
            SystemPrompt = systemPrompt,
            MaxTokens = 500,
        };

        var result = await server.SampleAsync(samplingParams, ct);
        var responseText = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";

        return ParseToolNamesFromSamplingResponse(responseText);
    }

    /// <summary>
    /// Parses tool names from a sampling response. Resilient to noise
    /// (system prompt leakage per copilot-cli#2467, preamble text).
    /// </summary>
    private List<ToolCatalogEntry>? ParseToolNamesFromSamplingResponse(string responseText)
    {
        // Try to find a JSON array in the response
        var startBracket = responseText.IndexOf('[');
        var endBracket = responseText.LastIndexOf(']');

        if (startBracket >= 0 && endBracket > startBracket)
        {
            var jsonPart = responseText[startBracket..(endBracket + 1)];
            try
            {
                var names = JsonSerializer.Deserialize<List<string>>(jsonPart);
                if (names is not null)
                {
                    return _catalog.GetToolDetails(names).ToList();
                }
            }
            catch
            {
                // JSON parse failed — try line-by-line extraction
            }
        }

        // Fallback: look for tool names that exist in our catalog
        var results = new List<ToolCatalogEntry>();
        var words = responseText.Split([',', '\n', '\r', '"', ' ', '[', ']'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            var trimmed = word.Trim();
            var entry = _catalog.GetEntry(trimmed);
            if (entry is not null && !results.Contains(entry))
                results.Add(entry);
        }

        return results.Count > 0 ? results : null;
    }

    /// <summary>
    /// Simple keyword matching fallback. Tokenizes the query and scores tools by keyword overlap.
    /// </summary>
    private static IEnumerable<ToolCatalogEntry> KeywordMatch(
        string query, IEnumerable<ToolCatalogEntry> candidates, int top)
    {
        var queryWords = query.ToLowerInvariant()
            .Split(new[] { ' ', ',', '.', '?', '!', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2) // skip tiny words
            .ToHashSet();

        if (queryWords.Count == 0)
            return candidates.Take(top);

        return candidates
            .Select(entry =>
            {
                var text = $"{entry.Descriptor.Name} {entry.Descriptor.Description} {entry.Workflow}"
                    .ToLowerInvariant();
                var score = queryWords.Count(w => text.Contains(w));
                return (entry, score);
            })
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(top)
            .Select(x => x.entry);
    }

    /// <summary>
    /// Builds a structured guidance response from matched catalog entries.
    /// </summary>
    public static string BuildGuidanceResponse(List<ToolCatalogEntry> entries, string? query)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrEmpty(query))
            sb.AppendLine($"Found {entries.Count} matching operation(s) for: \"{query}\"");
        else
            sb.AppendLine($"Found {entries.Count} operation(s):");

        sb.AppendLine();

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var flags = new List<string>();
            if (entry.Descriptor.Annotations?.DestructiveHint == true) flags.Add("⚠️ DESTRUCTIVE");
            if (entry.Descriptor.Annotations?.ReadOnlyHint == true) flags.Add("📖 read-only");

            sb.AppendLine($"**{i + 1}. {entry.Descriptor.Name}** {string.Join(" ", flags)}");
            sb.AppendLine(entry.Descriptor.Description);
            sb.AppendLine($"Workflow: {entry.Workflow}");
            sb.AppendLine($"Parameters: {entry.InputSchema.GetRawText()}");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine("To use immediately: call `execute_operation` with the operation name and arguments above.");
        sb.AppendLine("On your next turn: the operations above will be available as direct tool calls.");

        return sb.ToString();
    }
}
