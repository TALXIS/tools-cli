using System.Reflection;

namespace TALXIS.CLI.Features.Docs;

/// <summary>
/// Reads a public skill (doc) from the embedded resources and adds the shared note after its title
/// (how a command runs as CLI or MCP tool, and that names and layouts in the docs are defaults).
/// The note lives once, in Skills/_shared-note.md, so the docs themselves do not repeat it. Both <c>txc docs get</c> and the MCP <c>get_skill_details</c> tool
/// read docs through here, so a reader sees the same text on either route.
/// </summary>
public static class SkillContent
{
    private const string SharedNoteResource = "Skills._shared-note.md";

    /// <summary>
    /// Returns the doc with the shared note after its title, or null when there is no such doc.
    /// </summary>
    public static string? Load(string skillId)
    {
        var assembly = typeof(SkillContent).Assembly;

        // Names starting with "_" are shared parts (the note), not docs.
        if (string.IsNullOrWhiteSpace(skillId) || skillId.StartsWith('_'))
            return null;

        var body = ReadResource(assembly, name =>
            name.EndsWith($"Skills.{skillId}.md", StringComparison.Ordinal) ||
            name.EndsWith($"Skills.{skillId.Replace("-", "_")}.md", StringComparison.Ordinal));
        if (body is null)
            return null;

        var note = ReadResource(assembly, name => name.EndsWith(SharedNoteResource, StringComparison.Ordinal));
        return note is null ? body : InsertAfterTitle(body, note.Trim());
    }

    /// <summary>
    /// Places the note on its own paragraph right after the "# Title" line (or on top when the doc has no title line).
    /// </summary>
    internal static string InsertAfterTitle(string body, string note)
    {
        var lineEnd = body.IndexOf('\n');
        if (!body.StartsWith("# ", StringComparison.Ordinal) || lineEnd < 0)
            return note + "\n\n" + body;

        var title = body[..lineEnd].TrimEnd('\r');
        var rest = body[(lineEnd + 1)..];
        return title + "\n\n" + note + "\n" + (rest.StartsWith('\n') ? "" : "\n") + rest;
    }

    private static string? ReadResource(Assembly assembly, Func<string, bool> matches)
    {
        var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(matches);
        if (resourceName is null)
            return null;

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            return null;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
