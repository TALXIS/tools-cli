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
    /// and injects matched tools so clients discover them by re-fetching tools/list on subsequent turns.
    /// </summary>
    /// <param name="query">Natural language description of what the user wants to do.</param>
    /// <param name="workflow">Optional explicit workflow filter.</param>
    /// <param name="top">Maximum number of tools to return.</param>
    /// <param name="server">MCP server instance for sampling.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A CallToolResult with structured guidance text.</returns>
    public async Task<CallToolResult> HandleAsync(
        string query, string? workflow, int top, McpServer server, CancellationToken ct)
    {
        IEnumerable<ToolCatalogEntry> matchedEntries;
        string? recipeText = null;

        if (!string.IsNullOrEmpty(workflow))
        {
            // Explicit workflow — return all tools for that workflow
            matchedEntries = _catalog.GetEntriesByWorkflow(workflow);

            if (string.IsNullOrEmpty(query))
            {
                // No query with explicit workflow — return compact listing (no schemas) to avoid token bloat
                var workflowEntries = matchedEntries.ToList();
                var toolDefs = workflowEntries.Select(McpToolRegistry.BuildToolDefinition).ToList();
                _activeToolSet.InjectTools(toolDefs);
                var compactResponse = BuildCompactListingResponse(workflowEntries, workflow);
                return new CallToolResult
                {
                    Content = [new TextContentBlock { Text = compactResponse }]
                };
            }
        }
        else
        {
            // Try sampling first, fall back to keyword matching
            var (tools, recipe) = await DiscoverToolsAsync(query, top, server, ct, "guide");
            matchedEntries = tools;
            recipeText = recipe;
        }

        var entries = matchedEntries.ToList();
        if (entries.Count == 0)
        {
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = $"No matching tools found for: {query}\n\nAvailable workflows: local-development, environment-inspection, environment-mutation, data-operations, deployment, configuration, changeset-management\n\nTry calling with a workflow parameter to see all tools in a domain." }]
            };
        }

        // Inject matched tools into ActiveToolSet for direct calling on subsequent turns
        var toolDefinitions = entries.Select(McpToolRegistry.BuildToolDefinition).ToList();
        _activeToolSet.InjectTools(toolDefinitions);

        // Build structured response (includes recipe when available from sampling)
        var response = BuildGuidanceResponse(entries, query, recipeText);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = response }]
        };
    }

    /// <summary>
    /// Handles a domain-specific guide call. Scopes to a specific workflow's catalog.
    /// </summary>
    public async Task<CallToolResult> HandleWorkflowGuideAsync(
        string workflowScope, string query, int top, McpServer server, CancellationToken ct, string guideName = "guide")
    {
        IEnumerable<ToolCatalogEntry> matchedEntries;
        string? recipeText = null;

        if (string.IsNullOrEmpty(query))
        {
            // No query — return compact listing (no schemas) to avoid token bloat
            var allEntries = _catalog.GetEntriesByWorkflow(workflowScope).ToList();
            var toolDefs = allEntries.Select(McpToolRegistry.BuildToolDefinition).ToList();
            _activeToolSet.InjectTools(toolDefs);
            var compactResponse = BuildCompactListingResponse(allEntries, workflowScope);
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = compactResponse }]
            };
        }
        else
        {
            // Use sampling with scoped catalog
            var (tools, recipe) = await DiscoverToolsWithScopedCatalogAsync(
                query, top, workflowScope, server, ct, guideName);
            matchedEntries = tools;
            recipeText = recipe;
        }

        var entries = matchedEntries.ToList();

        var toolDefinitions = entries.Select(McpToolRegistry.BuildToolDefinition).ToList();
        _activeToolSet.InjectTools(toolDefinitions);

        var response = BuildGuidanceResponse(entries, query, recipeText);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = response }]
        };
    }

    /// <summary>
    /// Discovers tools matching the query using MCP sampling.
    /// The client's LLM selects the most relevant tools and produces a step-by-step recipe.
    /// Sampling is required — clients without sampling support will receive an error.
    /// </summary>
    /// <param name="guideName">The guide tool name, used to load relevant internal skills.</param>
    /// <returns>A tuple of matched tools and optional recipe text from the sampling response.</returns>
    private async Task<(IEnumerable<ToolCatalogEntry> tools, string? recipe)> DiscoverToolsAsync(
        string query, int top, McpServer server, CancellationToken ct, string guideName = "guide")
    {
        var skillsContext = _reasoningEngine?.GetSkillsContext(guideName) ?? "";
        var (samplingResult, recipe) = await SampleToolSelectionAsync(query, _catalog.GetCatalogPrompt(), skillsContext, top, server, ct);

        if (samplingResult is null || samplingResult.Count == 0)
            throw new InvalidOperationException("Sampling returned no results. Ensure the MCP client supports sampling.");

        return (samplingResult, recipe);
    }

    /// <summary>
    /// Discovers tools using a workflow-scoped catalog via MCP sampling.
    /// </summary>
    private async Task<(IEnumerable<ToolCatalogEntry> tools, string? recipe)> DiscoverToolsWithScopedCatalogAsync(
        string query, int top, string workflow, McpServer server, CancellationToken ct, string guideName = "guide")
    {
        var skillsContext = _reasoningEngine?.GetSkillsContext(guideName) ?? "";
        var catalogPrompt = _catalog.GetWorkflowCatalogPrompt(workflow);
        var (samplingResult, recipe) = await SampleToolSelectionAsync(query, catalogPrompt, skillsContext, top, server, ct);

        if (samplingResult is null || samplingResult.Count == 0)
            throw new InvalidOperationException("Sampling returned no results. Ensure the MCP client supports sampling.");

        return (samplingResult, recipe);
    }

    /// <summary>
    /// Sends a sampling/createMessage request to the client's LLM for tool selection and recipe generation.
    /// The client's own LLM understands the user's intent, selects the most relevant tools,
    /// and produces a step-by-step recipe with concrete execute_operation calls.
    /// Internal skills are injected into the system prompt for proprietary reasoning context.
    /// </summary>
    /// <returns>A tuple of matched tools and optional recipe text extracted from the sampling response.</returns>
    private async Task<(List<ToolCatalogEntry>? tools, string? recipe)> SampleToolSelectionAsync(
        string query, string catalogPrompt, string internalSkillsContext, int top, McpServer server, CancellationToken ct)
    {
        var systemPrompt = $@"You are a Power Platform development assistant. Given the user's task and available operations, produce a step-by-step RECIPE.

FORMAT YOUR RESPONSE AS:
1. A JSON array of the {top} most relevant tool names on the FIRST LINE (for tool injection)
2. Then a blank line
3. Then a numbered recipe with concrete steps

RECIPE RULES:
- Each step should specify the exact execute_operation call with operation name
- Include realistic parameter values based on the user's query
- Add validation checkpoints (e.g., ""Build to validate: dotnet build"")
- Prefer LOCAL workspace operations over environment operations
- Environment operations take 30s-5min — only use for inspection/deployment
- If a step depends on a previous step's output, say so
- Include error recovery hints for common failures (TALXISXSD001 = schema validation, TALXISGUID001 = duplicate GUIDs)

{catalogPrompt}{internalSkillsContext}";

        var samplingParams = new CreateMessageRequestParams
        {
            Messages =
            [
                new SamplingMessage
                {
                    Role = Role.User,
                    Content = [new TextContentBlock { Text = $"Select the most relevant tools and produce a recipe for this task: {query}" }]
                }
            ],
            SystemPrompt = systemPrompt,
            MaxTokens = 1500,
        };

        var result = await server.SampleAsync(samplingParams, ct);
        var responseText = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";

        return ParseToolNamesAndRecipeFromSamplingResponse(responseText);
    }

    /// <summary>
    /// Parses tool names and recipe text from a sampling response.
    /// Expects a JSON array on the first line (for tool injection) followed by optional recipe text.
    /// Resilient to noise (system prompt leakage per copilot-cli#2467, preamble text).
    /// </summary>
    /// <returns>A tuple of matched tools and optional recipe text.</returns>
    private (List<ToolCatalogEntry>? tools, string? recipe) ParseToolNamesAndRecipeFromSamplingResponse(string responseText)
    {
        List<ToolCatalogEntry>? tools = null;
        string? recipe = null;

        // Try to find a JSON array in the response
        var startBracket = responseText.IndexOf('[');
        var endBracket = responseText.IndexOf(']', startBracket >= 0 ? startBracket : 0);

        if (startBracket >= 0 && endBracket > startBracket)
        {
            var jsonPart = responseText[startBracket..(endBracket + 1)];
            try
            {
                var names = JsonSerializer.Deserialize<List<string>>(jsonPart);
                if (names is not null)
                {
                    tools = _catalog.GetToolDetails(names).ToList();
                }
            }
            catch
            {
                // JSON parse failed — try line-by-line extraction below
            }

            // Extract recipe text: everything after the JSON array, trimmed of leading blank lines
            var afterJson = responseText[(endBracket + 1)..].TrimStart('\r', '\n');
            if (!string.IsNullOrWhiteSpace(afterJson))
            {
                recipe = afterJson;
            }
        }

        return (tools, recipe);
    }



    /// <summary>
    /// Builds a compact listing (name + description only, no schemas) for browsing a full workflow.
    /// Use this when the user didn't provide a query and is just exploring what's available.
    /// </summary>
    internal static string BuildCompactListingResponse(List<ToolCatalogEntry> entries, string? workflow)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## Available operations{(workflow is not null ? $" — {workflow}" : "")}");
        sb.AppendLine($"{entries.Count} operations available. Call this guide with a specific query to get full parameter details.\n");

        foreach (var entry in entries)
        {
            var flags = new List<string>();
            if (entry.Descriptor.Annotations?.DestructiveHint == true) flags.Add("⚠️ DESTRUCTIVE");
            if (entry.Descriptor.Annotations?.ReadOnlyHint == true) flags.Add("📖 read-only");

            sb.AppendLine($"- **{entry.Descriptor.Name}** {string.Join(" ", flags)}: {entry.Descriptor.Description}");
        }

        sb.AppendLine("\n---");
        sb.AppendLine("To use a specific operation: call this guide with a query describing your task, or call `execute_operation` directly if you know the operation name and parameters.");
        return sb.ToString();
    }

    /// <summary>
    /// Builds a structured guidance response from matched catalog entries.
    /// When recipe text is available (from sampling), it is prepended as a step-by-step plan.
    /// </summary>
    /// <param name="entries">Matched catalog entries with full schemas.</param>
    /// <param name="query">The original user query.</param>
    /// <param name="recipeText">Optional recipe text produced by sampling. Null for keyword-matched results.</param>
    public static string BuildGuidanceResponse(List<ToolCatalogEntry> entries, string? query, string? recipeText = null)
    {
        var sb = new StringBuilder();

        // Prepend recipe when available (only from sampling-based discovery)
        if (!string.IsNullOrWhiteSpace(recipeText))
        {
            sb.AppendLine($"RECIPE: {query}");
            sb.AppendLine();
            sb.AppendLine(recipeText.TrimEnd());
            sb.AppendLine();
            sb.AppendLine("---");
        }

        sb.AppendLine($"TOOLS (for execute_operation): {entries.Count} operation(s)");
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
        sb.AppendLine("On your next turn: discovered operations may be available as direct tool calls. You can always use `execute_operation` to run any discovered operation immediately.");

        return sb.ToString();
    }
}
