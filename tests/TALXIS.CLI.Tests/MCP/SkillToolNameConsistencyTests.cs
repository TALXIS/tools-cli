#pragma warning disable MCPEXP001

using System.Reflection;
using System.Text.RegularExpressions;
using TALXIS.CLI.MCP;
using Xunit;

namespace TALXIS.CLI.Tests.MCP;

/// <summary>
/// Regression tests guarding against the failure mode from issue #114: skills (internal
/// reasoning markdown injected into LLM sampling prompts, and public skills served via
/// get_skill_details) drifted out of sync with the actual MCP tool catalog after a CLI
/// command rename (commit 409513a, show -> get). The LLM trusted the stale skills, called
/// non-existent tools, then fell back to raw SQL against asyncoperation.
///
/// These tests scan every skill markdown file for tool-name-shaped tokens and assert each
/// one exists in the live tool catalog.
/// </summary>
public class SkillToolNameConsistencyTests
{
    // Tool names are derived from CliCommand hierarchies. Top-level command groups in the
    // catalog (workspace, environment, config, data, docs) plus the standalone "component_*"
    // and "guide_*" tools. Hyphenated MCP-specific tools (copilot-instructions) don't match
    // this shape and are skipped — they have no rename risk because they aren't generated
    // from CLI attributes.
    // Tool names follow `<group>_<segment>(_<segment>)+` where each segment may itself contain
    // hyphens (e.g. `environment_solution_uninstall-check`, `config_auth_add-service-principal`).
    // Both underscore and hyphen act as intra-name separators in real catalog names.
    private static readonly Regex ToolNamePattern = new(
        @"\b(workspace|environment|config|data|docs|component|guide)(?:_[a-z0-9]+(?:-[a-z0-9]+)*)+\b",
        RegexOptions.Compiled);

    private readonly HashSet<string> _validToolNames;
    private readonly HashSet<string> _commandNames;

    // MCP-specific tools registered directly in Program.cs (not via McpToolRegistry.Catalog).
    // Keep this list in sync with the IsGuideTool helper and the get_skill_details/execute_operation
    // registrations in Program.cs.
    private static readonly string[] McpSpecificToolNames =
    {
        "guide",
        "guide_workspace",
        "guide_environment",
        "guide_deployment",
        "guide_data",
        "guide_config",
        "execute_operation",
        "get_skill_details",
    };

    // Commands marked [McpIgnore] are not MCP tools but are real CLI commands the docs may name.
    private static readonly string[] CliOnlyCommandNames =
    {
        "config_auth_login",
        "config_clear",
        "data_transform_server_start",
    };

    // A `txc <words>` command written in a code span, or at the start of a line in a code block.
    // The words are the command path plus any plain words after it (for example a template name);
    // arguments, options and placeholders start with a character outside [a-z] and end the match.
    private static readonly Regex TxcCommandPattern = new(
        @"(?:`|^\s*)txc ([a-z][a-z0-9-]*(?: [a-z][a-z0-9-]*)*)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public SkillToolNameConsistencyTests()
    {
        var registry = new McpToolRegistry();
        _commandNames = registry.Catalog
            .GetAllEntries()
            .Select(e => e.Descriptor.Name)
            .Concat(CliOnlyCommandNames)
            .ToHashSet(StringComparer.Ordinal);
        _validToolNames = registry.Catalog
            .GetAllEntries()
            .Select(e => e.Descriptor.Name)
            .Concat(McpSpecificToolNames)
            .ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void InternalSkills_AllReferencedToolNames_ExistInCatalog()
    {
        var skills = LoadEmbeddedMarkdown(
            typeof(GuideReasoningEngine).Assembly,
            "TALXIS.CLI.MCP.Skills.Internal.");

        AssertAllReferencesResolve(skills, "internal");
    }

    [Fact]
    public void PublicSkills_AllReferencedToolNames_ExistInCatalog()
    {
        // Trigger Docs assembly load via PublicSkillLoader (uses the same lookup pattern as production).
        var loader = new PublicSkillLoader();
        loader.LoadIndex();

        var docsAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "TALXIS.CLI.Features.Docs");

        // If the Docs assembly isn't loaded, there are no public skills to check —
        // that's a different bug, covered by other tests.
        if (docsAssembly is null) return;

        var skills = LoadEmbeddedMarkdown(docsAssembly, "TALXIS.CLI.Features.Docs.Skills.");

        AssertAllReferencesResolve(skills, "public");
    }

    // Public docs are served both by `txc docs get` and over MCP (get_skill_details), so each one
    // says up front that a command can be run as `txc <command>` or, with the MCP server, as the
    // tool named by joining the command words with "_". Keep the wording in one place: change it
    // here and in every doc together.
    private const string RunningPreamble =
        "> **Running these commands.** Commands are written for the CLI: `txc <command>` (`txc "
        + "<command> --help` lists its arguments and options). With the TALXIS MCP server connected, "
        + "most commands are also tools, named by joining the command words with `_` (`txc workspace "
        + "component create` is `workspace_component_create`; hyphens stay, as in "
        + "`environment_solution_uninstall-check`). If a tool is not in your tool list, run it through "
        + "the server's `execute_operation` tool. A tool's input schema names its arguments, and those "
        + "names can differ from the CLI options. A link such as "
        + "`[plugin-development](plugin-development.md)` is a doc id: `txc docs get plugin-development` "
        + "prints it.";

    [Fact]
    public void PublicSkills_StartWithTheSameRunningPreamble()
    {
        var loader = new PublicSkillLoader();
        loader.LoadIndex();

        var docsAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "TALXIS.CLI.Features.Docs");
        if (docsAssembly is null) return;

        var skills = LoadEmbeddedMarkdown(docsAssembly, "TALXIS.CLI.Features.Docs.Skills.");
        Assert.NotEmpty(skills);

        // The preamble must be the first paragraph after the title.
        var missing = skills
            .Where(s =>
            {
                var lines = s.Value.Replace("\r\n", "\n").Split('\n');
                return lines.Length < 3 || !lines[0].StartsWith("# ") || lines[1] != "" || lines[2] != RunningPreamble;
            })
            .Select(s => $"  {s.Key}.md")
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These public docs do not start with the shared 'Running these commands' preamble "
            + "(title, blank line, preamble):\n" + string.Join("\n", missing));
    }

    [Fact]
    public void PublicSkills_AllTxcCommandPaths_ExistInCommandTree()
    {
        // The docs are written in CLI form (`txc a b c`), which the tool-name test above cannot see.
        // A command path is valid when a leading run of its words is a command, or when it is a
        // command group named in prose (`txc config profile`).
        var loader = new PublicSkillLoader();
        loader.LoadIndex();

        var docsAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "TALXIS.CLI.Features.Docs");
        if (docsAssembly is null) return;

        var skills = LoadEmbeddedMarkdown(docsAssembly, "TALXIS.CLI.Features.Docs.Skills.");
        Assert.NotEmpty(skills);

        var unknown = new List<string>();
        foreach (var (skillId, content) in skills)
        {
            foreach (Match m in TxcCommandPattern.Matches(content))
            {
                var words = m.Groups[1].Value.Split(' ');
                var isCommand = Enumerable.Range(1, words.Length)
                    .Any(k => _commandNames.Contains(string.Join("_", words.Take(k))));
                if (isCommand) continue;

                var group = string.Join("_", words) + "_";
                if (_commandNames.Any(n => n.StartsWith(group, StringComparison.Ordinal))) continue;

                unknown.Add($"  public/{skillId}.md → 'txc {m.Groups[1].Value}'");
            }
        }

        Assert.True(
            unknown.Count == 0,
            "Public docs name txc commands that do not exist in the command tree. "
            + "This usually means a command was renamed or mistyped. Unknown commands:\n"
            + string.Join("\n", unknown.Distinct()));
    }

    private void AssertAllReferencesResolve(
        IReadOnlyDictionary<string, string> skills,
        string skillCategory)
    {
        Assert.NotEmpty(skills);

        var stale = new List<string>();
        foreach (var (skillId, content) in skills)
        {
            var referencedNames = ToolNamePattern.Matches(content)
                .Select(m => m.Value)
                .Distinct(StringComparer.Ordinal);

            foreach (var name in referencedNames)
            {
                if (!_validToolNames.Contains(name))
                {
                    var suggestion = SuggestNearest(name);
                    stale.Add($"  {skillCategory}/{skillId}.md → '{name}'{(suggestion is null ? "" : $" (did you mean '{suggestion}'?)")}");
                }
            }
        }

        Assert.True(
            stale.Count == 0,
            "Skills reference tool names that do not exist in the MCP tool catalog. "
            + "This usually means a CLI command was renamed and the skill was not updated. "
            + "Stale references:\n"
            + string.Join("\n", stale));
    }

    /// <summary>
    /// Returns the catalog name with the smallest case-insensitive edit distance from the
    /// referenced name. Useful for nudging contributors toward the right tool after a rename
    /// (e.g. "_show" → "_get", "_check" → "-check"). Returns null when no candidate is within
    /// a reasonable distance.
    /// </summary>
    private string? SuggestNearest(string referenced)
    {
        string? best = null;
        int bestDistance = int.MaxValue;
        int threshold = Math.Max(3, referenced.Length / 3);

        foreach (var candidate in _validToolNames)
        {
            // Cheap shape filter — same first segment, similar length
            if (Math.Abs(candidate.Length - referenced.Length) > threshold) continue;
            int d = LevenshteinDistance(referenced, candidate);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = candidate;
            }
        }

        return bestDistance <= threshold ? best : null;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) dp[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1), dp[i - 1, j - 1] + cost);
            }
        return dp[a.Length, b.Length];
    }

    private static IReadOnlyDictionary<string, string> LoadEmbeddedMarkdown(
        Assembly assembly,
        string resourcePrefix)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(resourcePrefix) || !resourceName.EndsWith(".md"))
                continue;

            var skillId = resourceName[resourcePrefix.Length..^3];
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null) continue;

            using var reader = new StreamReader(stream);
            result[skillId] = reader.ReadToEnd();
        }
        return result;
    }
}
