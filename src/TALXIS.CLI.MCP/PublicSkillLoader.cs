using System.Reflection;
using System.Text.Json;

namespace TALXIS.CLI.MCP;

/// <summary>
/// Loads public skills from embedded resources in the TALXIS.CLI.Features.Docs assembly.
/// Public skills are developer-facing knowledge accessible via the get_skill_details MCP tool
/// and the txc docs CLI command.
/// </summary>
public class PublicSkillLoader
{
    private readonly Dictionary<string, PublicSkillEntry> _skills = new();
    private List<PublicSkillIndexEntry>? _index;

    /// <summary>
    /// Loads the skill index and caches skill metadata.
    /// Skill content is loaded on demand to minimize startup overhead.
    /// </summary>
    public void LoadIndex()
    {
        // The Docs assembly contains the Skills/ embedded resources
        var docsAssembly = FindDocsAssembly();
        if (docsAssembly is null) return;

        var indexResourceName = docsAssembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("Skills.index.json"));

        if (indexResourceName is null) return;

        using var stream = docsAssembly.GetManifestResourceStream(indexResourceName);
        if (stream is null) return;

        _index = JsonSerializer.Deserialize<List<PublicSkillIndexEntry>>(stream,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        if (_index is null) return;

        foreach (var entry in _index)
        {
            _skills[entry.Id] = new PublicSkillEntry
            {
                Id = entry.Id,
                Title = entry.Title,
                Summary = entry.Summary,
                Tags = entry.Tags,
                Assembly = docsAssembly
            };
        }
    }

    /// <summary>
    /// Gets the full content of a skill by ID.
    /// </summary>
    public string? GetSkillContent(string skillId)
    {
        if (!_skills.TryGetValue(skillId, out var entry))
            return null;

        // Lazy load content from embedded resource
        if (entry.Content is null)
        {
            var resourceName = entry.Assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith($"Skills.{skillId}.md") || n.EndsWith($"Skills.{skillId.Replace("-", "_")}.md"));

            if (resourceName is null) return null;

            using var stream = entry.Assembly.GetManifestResourceStream(resourceName);
            if (stream is null) return null;

            using var reader = new StreamReader(stream);
            entry.Content = reader.ReadToEnd();
        }

        return entry.Content;
    }

    /// <summary>
    /// Gets the skill index (all skill metadata without full content).
    /// </summary>
    public IReadOnlyList<PublicSkillIndexEntry> GetIndex()
    {
        return _index ?? [];
    }

    /// <summary>
    /// Builds a compact skills index string for inclusion in ServerInstructions.
    /// </summary>
    public string GetSkillsIndexPrompt()
    {
        if (_index is null || _index.Count == 0)
            return string.Empty;

        var lines = _index.Select(s => $"- {s.Id}: {s.Summary}");
        return "Available skills (call get_skill_details for full instructions):\n" + string.Join("\n", lines);
    }

    /// <summary>
    /// Number of loaded skills.
    /// </summary>
    public int Count => _skills.Count;

    /// <summary>
    /// Finds the TALXIS.CLI.Features.Docs assembly from loaded assemblies.
    /// </summary>
    private static Assembly? FindDocsAssembly()
    {
        // Try to find by name in loaded assemblies
        return AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "TALXIS.CLI.Features.Docs")
            // Fallback: load explicitly (the MCP project references CLI which references Docs)
            ?? TryLoadDocsAssembly();
    }

    private static Assembly? TryLoadDocsAssembly()
    {
        try
        {
            return Assembly.Load("TALXIS.CLI.Features.Docs");
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Index entry for a public skill (from index.json).
/// </summary>
public class PublicSkillIndexEntry
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string[] Tags { get; set; } = [];
}

/// <summary>
/// Internal entry with lazy-loaded content.
/// </summary>
internal class PublicSkillEntry
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public required string[] Tags { get; init; }
    public required Assembly Assembly { get; init; }
    public string? Content { get; set; }
}
