using System.Reflection;
using System.Xml.Linq;
using DotMake.CommandLine;
using TALXIS.CLI.Core;
using Xunit;

namespace TALXIS.CLI.Tests.Architecture;

/// <summary>
/// Tests that enforce project layering rules from CONTRIBUTING.md:
/// - Features do NOT reference other Features (except the documented exception below)
/// - Destructive commands with <c>--yes</c> must have <c>[CliDestructive("…")]</c>
/// </summary>
public class LayeringTests
{
    private static readonly string SrcRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src"));

    /// <summary>
    /// CONTRIBUTING.md: "No feature references another feature. Shared logic goes into Core."
    /// </summary>
    [Fact]
    public void FeatureProjects_ShouldNotReferenceOtherFeatures()
    {
        var allowedExceptions = new HashSet<(string From, string To)>();

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
    /// Commands with a <c>--yes</c> flag are destructive operations. They must carry
    /// <c>[CliDestructive("…")]</c> so MCP clients can prompt for human-in-the-loop
    /// confirmation before execution. The <c>--yes</c> flag itself provides
    /// server-side defense in depth.
    /// </summary>
    [Fact]
    public void DestructiveCommands_WithYesFlag_MustHaveDestructiveAnnotation()
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
            .Where(t =>
            {
                var destructive = t.GetCustomAttribute<CliDestructiveAttribute>();
                return destructive is null;
            })
            .Select(t => t.FullName)
            .ToList();

        Assert.True(violations.Count == 0,
            $"Commands with --yes flag must have [CliDestructive(\"…\")] " +
            $"so MCP clients prompt for confirmation:\n" +
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
