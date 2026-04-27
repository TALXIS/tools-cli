using System.Reflection;

namespace TALXIS.CLI.MCP;

/// <summary>
/// Loads internal reasoning skills from embedded resources and provides them to guide tools
/// for inclusion in sampling prompts. Internal skills encode Power Platform development
/// expertise, decision trees, and workflow patterns. They are NOT exposed to clients.
/// </summary>
public class GuideReasoningEngine
{
    private readonly Dictionary<string, string> _internalSkills = new();

    /// <summary>
    /// Mapping of guide tool names to their relevant internal skill IDs.
    /// Each guide loads only the skills relevant to its domain.
    /// </summary>
    private static readonly Dictionary<string, string[]> _guideSkillMappings = new()
    {
        ["guide"] = ["local-first-philosophy"],
        ["guide_workspace"] = ["local-first-philosophy", "schema-workflow"],
        ["guide_environment"] = ["troubleshooting-patterns", "solution-management"],
        ["guide_deployment"] = ["deployment-sequence", "solution-management"],
        ["guide_data"] = ["data-migration-workflow"],
        ["guide_config"] = [],
    };

    /// <summary>
    /// Loads all internal skills from embedded resources at startup.
    /// </summary>
    public void LoadSkills()
    {
        var assembly = typeof(GuideReasoningEngine).Assembly;
        var prefix = "TALXIS.CLI.MCP.Skills.Internal.";

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(prefix) || !resourceName.EndsWith(".md"))
                continue;

            // Extract skill ID from resource name (e.g., "TALXIS.CLI.MCP.Skills.Internal.local-first-philosophy.md" → "local-first-philosophy")
            var skillId = resourceName[prefix.Length..^3]; // remove prefix and .md

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null) continue;

            using var reader = new StreamReader(stream);
            _internalSkills[skillId] = reader.ReadToEnd();
        }
    }

    /// <summary>
    /// Gets the internal skills context for a specific guide tool, formatted for inclusion
    /// in the sampling prompt.
    /// </summary>
    /// <param name="guideName">The guide tool name (e.g., "guide_workspace").</param>
    /// <returns>Combined skill content, or empty string if no skills are mapped.</returns>
    public string GetSkillsContext(string guideName)
    {
        if (!_guideSkillMappings.TryGetValue(guideName, out var skillIds) || skillIds.Length == 0)
            return string.Empty;

        var parts = new List<string>();
        foreach (var id in skillIds)
        {
            if (_internalSkills.TryGetValue(id, out var content))
            {
                parts.Add(content);
            }
        }

        return parts.Count > 0
            ? "\n\n--- INTERNAL DEVELOPMENT GUIDELINES ---\n\n" + string.Join("\n\n---\n\n", parts)
            : string.Empty;
    }

    /// <summary>
    /// Number of loaded internal skills.
    /// </summary>
    public int Count => _internalSkills.Count;
}
