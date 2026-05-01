using System.Linq;
using System.Reflection;
using DotMake.CommandLine;
using TALXIS.CLI;
using TALXIS.CLI.Features.Environment.Deployment;
using TALXIS.CLI.Features.Workspace;
using TALXIS.CLI.Features.Workspace.Metamodel;
using Xunit;

namespace TALXIS.CLI.Tests.Environment;

/// <summary>
/// Verifies that reserved skeleton command classes compile and carry the
/// required <c>[CliCommand]</c> attribute, but are NOT reachable from the
/// CLI command tree (i.e. not referenced by any parent's <c>Children</c>
/// array). Unreachability is the mechanism that keeps the design pinned
/// in code without exposing half-built commands to users.
///
/// If any of these assertions fail, see <c>CONTRIBUTING.md</c> — activating
/// a skeleton must be a deliberate two-edit change (add to <c>Children</c>
/// and implement), not an accidental re-wiring.
/// </summary>
public class SkeletonScaffoldTests
{
    public static IEnumerable<object[]> Skeletons =>
        new[]
        {
            new object[] { typeof(DeploymentPatchCliCommand) },
            new object[] { typeof(WorkspaceLanguageServerCliCommand) },
            new object[] { typeof(MetamodelCliCommand) },
            new object[] { typeof(MetamodelDescribeCliCommand) },
            new object[] { typeof(MetamodelListCliCommand) },
        };

    [Theory]
    [MemberData(nameof(Skeletons))]
    public void Skeleton_IsDecoratedWithCliCommand(Type type)
    {
        var attr = type.GetCustomAttribute<CliCommandAttribute>(inherit: false);
        Assert.NotNull(attr);
    }

    [Theory]
    [MemberData(nameof(Skeletons))]
    public void Skeleton_IsUnreachableFromRoot(Type type)
    {
        var reachable = new HashSet<Type>();
        Collect(typeof(TxcCliCommand), reachable);

        Assert.DoesNotContain(type, reachable);
    }

    private static void Collect(Type type, HashSet<Type> visited)
    {
        if (!visited.Add(type)) return;
        var attr = type.GetCustomAttribute<CliCommandAttribute>(inherit: false);
        if (attr?.Children is null) return;
        foreach (var child in attr.Children)
        {
            Collect(child, visited);
        }
    }
}
