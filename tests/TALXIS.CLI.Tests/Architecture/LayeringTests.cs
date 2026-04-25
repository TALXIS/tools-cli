using System.Reflection;
using System.Xml.Linq;
using DotMake.CommandLine;
using TALXIS.CLI.Core;
using Xunit;

namespace TALXIS.CLI.Tests.Architecture;

/// <summary>
/// Tests that enforce project layering rules from CONTRIBUTING.md:
/// - Features do NOT reference other Features (except the documented exception below)
/// - Destructive commands with <c>--yes</c> must have <c>[McpIgnore]</c>
/// </summary>
public class LayeringTests
{
    private static readonly string SrcRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src"));

    /// <summary>
    /// CONTRIBUTING.md: "No feature references another feature. Shared logic goes into Core."
    /// 
    /// Known exception: Features.Environment and Features.Data reference Features.Config
    /// for ProfiledCliCommand. This should be resolved by moving ProfiledCliCommand to Core.
    /// </summary>
    [Fact]
    public void FeatureProjects_ShouldNotReferenceOtherFeatures()
    {
        // Known exception — ProfiledCliCommand lives in Features.Config but is
        // needed by Environment and Data. Track this as tech debt to move to Core.
        var allowedExceptions = new HashSet<(string From, string To)>
        {
            ("TALXIS.CLI.Features.Environment", "TALXIS.CLI.Features.Config"),
            ("TALXIS.CLI.Features.Data", "TALXIS.CLI.Features.Config"),
        };

        var featureProjects = Directory.GetFiles(SrcRoot, "TALXIS.CLI.Features.*.csproj", SearchOption.AllDirectories);
        var violations = new List<string>();

        foreach (var projectFile in featureProjects)
        {
            var projectName = Path.GetFileNameWithoutExtension(projectFile);
            var doc = XDocument.Load(projectFile);
            var refs = doc.Descendants("ProjectReference")
                .Select(e => e.Attribute("Include")?.Value)
                .Where(r => r != null)
                .Select(r => Path.GetFileNameWithoutExtension(r!))
                .Where(r => r.StartsWith("TALXIS.CLI.Features."));

            foreach (var referencedProject in refs)
            {
                if (!allowedExceptions.Contains((projectName, referencedProject)))
                {
                    violations.Add($"{projectName} → {referencedProject}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"Feature projects must not reference other Feature projects (CONTRIBUTING.md).\n" +
            $"Violations:\n  {string.Join("\n  ", violations)}\n\n" +
            "Move shared logic to TALXIS.CLI.Core instead.");
    }

    /// <summary>
    /// Commands with a <c>--yes</c> flag are destructive operations. They must be
    /// excluded from MCP agent access via <c>[McpIgnore]</c> to prevent automated
    /// tools from performing unconfirmed destructive actions.
    /// </summary>
    [Fact]
    public void DestructiveCommands_WithYesFlag_MustHaveMcpIgnore()
    {
        var commandAssemblies = new Assembly[]
        {
            typeof(TALXIS.CLI.Features.Config.ConfigCliCommand).Assembly,
            typeof(TALXIS.CLI.Features.Environment.EnvironmentCliCommand).Assembly,
            typeof(TALXIS.CLI.Features.Workspace.WorkspaceCliCommand).Assembly,
            typeof(TALXIS.CLI.Features.Data.DataCliCommand).Assembly,
        };

        var violations = commandAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t.GetCustomAttribute<CliCommandAttribute>() is not null && !t.IsAbstract)
            .Where(HasYesFlag)
            .Where(t => t.GetCustomAttribute<McpIgnoreAttribute>() is null)
            .Select(t => t.FullName)
            .ToList();

        Assert.True(violations.Count == 0,
            $"Commands with --yes flag must have [McpIgnore] to prevent MCP agents from performing destructive actions:\n" +
            string.Join("\n", violations!));
    }

    private static bool HasYesFlag(Type type)
    {
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Any(p =>
            {
                var attr = p.GetCustomAttribute<CliOptionAttribute>();
                return attr?.Name == "--yes" || (attr != null && p.Name == "Yes");
            });
    }
}
