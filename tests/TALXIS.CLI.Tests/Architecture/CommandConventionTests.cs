using System.Reflection;
using DotMake.CommandLine;
using TALXIS.CLI.Core;
using Xunit;

namespace TALXIS.CLI.Tests.Architecture;

/// <summary>
/// Architectural tests that enforce the <c>TxcLeafCommand</c> output contract.
/// These tests scan all assemblies for <c>[CliCommand]</c>-decorated classes and
/// verify they follow the conventions documented in <c>docs/output-contract.md</c>.
/// <para>
/// If any of these tests fail, a developer has added a command that does not
/// conform to the CLI's output contract. The fix is to inherit
/// <see cref="TxcLeafCommand"/> and implement <c>ExecuteAsync()</c>.
/// </para>
/// </summary>
public class CommandConventionTests
{
    /// <summary>
    /// All assemblies that may contain CLI commands.
    /// </summary>
    private static readonly Assembly[] CommandAssemblies =
    {
        typeof(TALXIS.CLI.Features.Config.ConfigCliCommand).Assembly,
        typeof(TALXIS.CLI.Features.Environment.EnvironmentCliCommand).Assembly,
        typeof(TALXIS.CLI.Features.Workspace.WorkspaceCliCommand).Assembly,
        typeof(TALXIS.CLI.Features.Data.DataCliCommand).Assembly,
        typeof(TALXIS.CLI.TxcCliCommand).Assembly,
    };

    /// <summary>
    /// Returns all types with <c>[CliCommand]</c> that are leaf commands
    /// (i.e., NOT routing/hub commands that just show help).
    /// A routing command is identified by having a <c>void Run(CliContext)</c>
    /// method or by being abstract.
    /// </summary>
    private static IEnumerable<Type> GetLeafCommandTypes()
    {
        return CommandAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t.GetCustomAttribute<CliCommandAttribute>() is not null)
            .Where(t => !t.IsAbstract)
            .Where(t => !IsRoutingCommand(t));
    }

    /// <summary>
    /// Routing commands have <c>void Run(CliContext context)</c> and just show help.
    /// </summary>
    private static bool IsRoutingCommand(Type type)
    {
        var runMethod = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Instance, null,
            new[] { typeof(CliContext) }, null);
        return runMethod is not null && runMethod.ReturnType == typeof(void);
    }

    /// <summary>
    /// Known exceptions that do not follow the TxcLeafCommand pattern
    /// (e.g., long-running servers, stubs).
    /// </summary>
    private static readonly HashSet<string> ExcludedTypes = new()
    {
        // Long-running server process — cannot follow request/response pattern
        "TransformServerStartCliCommand",
        // Reserved stubs that throw NotImplementedException
        "WorkspaceLanguageServerCliCommand",
        "MetamodelListCliCommand",
        "MetamodelDescribeCliCommand",
        "DeploymentPatchCliCommand",
    };

    [Fact]
    public void AllLeafCommands_MustInheritTxcLeafCommand()
    {
        var violations = GetLeafCommandTypes()
            .Where(t => !ExcludedTypes.Contains(t.Name))
            .Where(t => !typeof(TxcLeafCommand).IsAssignableFrom(t))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(violations.Count == 0,
            $"The following leaf commands do not inherit TxcLeafCommand:\n" +
            string.Join("\n", violations) +
            "\n\nAll leaf commands must extend TxcLeafCommand (or ProfiledCliCommand) " +
            "to ensure consistent output formatting, error handling, and the --format flag.");
    }

    [Fact]
    public void AllLeafCommands_MustNotDefineOwnRunAsync()
    {
        // TxcLeafCommand owns RunAsync(); leaf commands must not hide it
        var violations = GetLeafCommandTypes()
            .Where(t => !ExcludedTypes.Contains(t.Name))
            .Where(t => typeof(TxcLeafCommand).IsAssignableFrom(t))
            .Where(t => t.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                null, Type.EmptyTypes, null) is not null)
            .Select(t => t.FullName)
            .ToList();

        Assert.True(violations.Count == 0,
            $"The following commands define their own RunAsync() instead of using the base class:\n" +
            string.Join("\n", violations) +
            "\n\nImplement ExecuteAsync() instead. The base TxcLeafCommand.RunAsync() " +
            "provides standardized error handling and output formatting.");
    }

    [Fact]
    public void AllLeafCommands_MustImplementExecuteAsync()
    {
        var violations = GetLeafCommandTypes()
            .Where(t => !ExcludedTypes.Contains(t.Name))
            .Where(t => typeof(TxcLeafCommand).IsAssignableFrom(t))
            .Where(t => t.GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                null, Type.EmptyTypes, null) is null)
            .Select(t => t.FullName)
            .ToList();

        Assert.True(violations.Count == 0,
            $"The following commands extend TxcLeafCommand but don't implement ExecuteAsync():\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void AllLeafCommands_MustNotHaveJsonFlag()
    {
        // The --json flag is replaced by the global --format flag on TxcLeafCommand
        var violations = GetLeafCommandTypes()
            .Where(t => !ExcludedTypes.Contains(t.Name))
            .Where(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Any(p => p.GetCustomAttribute<CliOptionAttribute>() is { } attr
                    && (attr.Name == "--json" || p.Name == "Json")))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(violations.Count == 0,
            $"The following commands still have a --json flag:\n" +
            string.Join("\n", violations) +
            "\n\nUse OutputContext.IsJson and the global --format flag instead.");
    }

    [Fact]
    public void NoLeafCommand_ShouldDefineLocalJsonSerializerOptions()
    {
        // All output serialization should use TxcOutputJsonOptions.Default
        var violations = GetLeafCommandTypes()
            .Where(t => !ExcludedTypes.Contains(t.Name))
            .Where(t => t.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Any(f => f.FieldType.Name == "JsonSerializerOptions"))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(violations.Count == 0,
            $"The following commands define local JsonSerializerOptions:\n" +
            string.Join("\n", violations) +
            "\n\nUse TxcOutputJsonOptions.Default for output serialization.");
    }
}
