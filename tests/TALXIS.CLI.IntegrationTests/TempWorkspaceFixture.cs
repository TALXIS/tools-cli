using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace TALXIS.CLI.IntegrationTests;

/// <summary>
/// Scaffolds a temporary workspace with a Power Platform solution, entity and attributes
/// using the txc CLI. Shared across all tests in <see cref="SolutionConvertTests"/>.
///
/// Setup sequence:
///   1. Create a temp directory with a .sln (required by pp-solution post-actions)
///   2. txc workspace component create pp-solution (--output srcDir/TestSolution)
///   3. txc workspace component create pp-entity   (--output Declarations --name testentity)
///   4. txc workspace component create pp-entity-attribute (x3 attribute types)
///   5. Create a .gitignore so the auto-gitignore logic has a file to update
/// </summary>
public sealed class TempWorkspaceFixture : IAsyncLifetime
{
    private const string PublisherPrefix = "tst";
    private const string PublisherName = "TestPublisher";
    private const string EntityLogicalName = "testentity";

    // Entity schema name uses publisher prefix (set by pp-entity template)
    private string EntitySchemaName => $"{PublisherPrefix}_{EntityLogicalName}";

    /// <summary>Root of the temporary workspace.</summary>
    public string TempDir { get; private set; } = null!;

    /// <summary>
    /// Path to the scaffolded solution project folder.
    /// The Declarations subfolder within is passed as <c>--input</c> to <c>txc workspace solution convert</c>.
    /// </summary>
    public string SolutionDir { get; private set; } = null!;

    /// <summary>Path to the Declarations folder inside the solution (input for convert command).</summary>
    public string DeclarationsDir => Path.Combine(SolutionDir, "Declarations");

    public async Task InitializeAsync()
    {
        TempDir = Path.Combine(Path.GetTempPath(), $"txc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(TempDir);

        // Create a .gitignore so EnsureGitIgnored has a file to update
        await File.WriteAllTextAsync(Path.Combine(TempDir, ".gitignore"), "obj/\nbin/\n");

        // Init .sln — required by the pp-solution AddProjectsToSln post-action
        await RunDotnetAsync(["new", "sln", "--name", "TestWorkspace"], TempDir);

        var srcDir = Path.Combine(TempDir, "src");
        Directory.CreateDirectory(srcDir);

        // Scaffold Power Platform solution. Parent (srcDir) must exist; output must not exist yet.
        SolutionDir = Path.Combine(srcDir, "TestSolution");
        await CliRunner.RunAsync(
            ["workspace", "component", "create", "pp-solution",
             "--output", SolutionDir,
             "--param", $"PublisherName={PublisherName}",
             "--param", $"PublisherPrefix={PublisherPrefix}"]);

        // Scaffold entity into the Declarations folder (scripts use ./Other/Solution.xml relative to it)
        await CliRunner.RunAsync(
            ["workspace", "component", "create", "pp-entity",
             "--output", DeclarationsDir,
             "--name", EntityLogicalName,
             "--param", $"LogicalName={EntityLogicalName}",
             "--param", $"LogicalNamePlural={EntityLogicalName}s",
             "--param", "DisplayName=Test Entity",
             "--param", "DisplayNamePlural=Test Entities",
             "--param", $"PublisherPrefix={PublisherPrefix}",
             "--param", "Behavior=New",
             "--param", "SolutionRootPath=."]);

        // Add a WholeNumber attribute
        await AddAttributeAsync("count", "Count", "WholeNumber");

        // Add another WholeNumber attribute
        await AddAttributeAsync("quantity", "Quantity", "WholeNumber");
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(TempDir))
            Directory.Delete(TempDir, recursive: true);
        return Task.CompletedTask;
    }

    private Task AddAttributeAsync(string logicalName, string displayName, string attributeType, string? referencedEntityName = null)
    {
        var args = new List<string>
        {
            "workspace", "component", "create", "pp-entity-attribute",
            "--output", DeclarationsDir,
            "--name", logicalName,
            "--param", $"EntitySchemaName={EntitySchemaName}",
            "--param", $"LogicalName={logicalName}",
            "--param", $"DisplayName={displayName}",
            "--param", $"AttributeType={attributeType}",
            "--param", $"PublisherPrefix={PublisherPrefix}",
            "--param", "SolutionRootPath=."
        };

        if (referencedEntityName != null)
        {
            args.Add("--param");
            args.Add($"ReferencedEntityName={referencedEntityName}");
        }

        return CliRunner.RunAsync([.. args]);
    }

    private static async Task RunDotnetAsync(string[] args, string workingDirectory)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;

        // Drain both streams concurrently to prevent buffer-full deadlocks.
        var outTask = process.StandardOutput.ReadToEndAsync();
        var errTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();
        await Task.WhenAll(outTask, errTask);

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"dotnet {string.Join(' ', args)} failed in {workingDirectory}");
    }
}
