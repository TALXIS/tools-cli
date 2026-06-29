using System.ComponentModel;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Resolution;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Solution;

[CliIdempotent]
[CliLongRunning]
[CliCommand(
    Name = "pull",
    Description = "Pull a solution from the LIVE environment into the local source project: restores local file-name conventions, excludes binaries built from project references, and merges changes. Requires an active profile."
)]
public class SolutionPullCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(SolutionPullCliCommand));

    [CliArgument(Name = "project", Description = "Project directory (.cdsproj/.csproj) to sync into. Defaults to current directory. A bare solution unique name is also accepted, but project-reference binary exclusion then needs --output.")]
    [DefaultValue(".")]
    public string Project { get; set; } = ".";

    [CliOption(Name = "--output", Alias = "-o", Description = "Solution root folder to sync into. Overrides the project's SolutionRootPath. When neither is given, the project folder itself is used.", Required = false)]
    public string? Output { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var resolved = Resolve();
        if (resolved is null)
            return ExitValidationError;

        var (solutionName, solutionRoot, projectFile) = resolved.Value;

        var options = new SolutionPullOptions(solutionName, solutionRoot, projectFile);
        var service = TxcServices.Get<ISolutionPullService>();
        var result = await service.PullAsync(Profile, options, CancellationToken.None).ConfigureAwait(false);

        var payload = new
        {
            status = "pulled",
            solution = solutionName,
            path = result.SolutionRootPath,
            normalizedAssemblies = result.NormalizedAssemblies,
            excludedRelationships = result.ExcludedRelationships,
            excludedBinaries = result.ExcludedBinaries,
            excludedWebResources = result.ExcludedWebResources,
            excludedPcfControls = result.ExcludedPcfControls,
            removedFiles = result.RemovedFiles,
        };

        OutputFormatter.WriteData(payload, _ =>
        {
#pragma warning disable TXC003
            OutputWriter.WriteLine($"Pulled solution '{solutionName}' → {result.SolutionRootPath}");
            WriteList("Normalized plugin assembly path(s)", result.NormalizedAssemblies);
            WriteList("Excluded standard system relationship(s)", result.ExcludedRelationships);
            WriteList("Excluded project-reference binary(ies)", result.ExcludedBinaries);
            WriteList("Excluded script-library web resource(s)", result.ExcludedWebResources);
            WriteList("Excluded PCF control(s)", result.ExcludedPcfControls);
            WriteList("Removed stale solution file(s)", result.RemovedFiles);

            static void WriteList(string label, IReadOnlyList<string> items)
            {
                if (items.Count == 0)
                    return;
                OutputWriter.WriteLine($"{label} ({items.Count}):");
                foreach (var item in items)
                    OutputWriter.WriteLine($"  - {item}");
            }
#pragma warning restore TXC003
        });

        return ExitSuccess;
    }

    private (string SolutionName, string SolutionRoot, string? ProjectFile)? Resolve()
    {
        if (!IsDirectoryPath(Project))
        {
            if (string.IsNullOrWhiteSpace(Output))
            {
                Logger.LogError("A bare solution name requires --output to specify the solution root folder.");
#pragma warning disable TXC003
                OutputWriter.WriteLine("Error: when passing a bare solution unique name, --output is required to specify the solution root folder.");
#pragma warning restore TXC003
                return null;
            }
            return (Project, Path.GetFullPath(Output), null);
        }

        var dirPath = Path.GetFullPath(Project);
        if (!Directory.Exists(dirPath))
        {
            Logger.LogError("Directory not found: {Path}.", dirPath);
            return null;
        }

        var projectFile = SolutionProjectResolver.FindProjectFile(dirPath);
        if (projectFile is null)
        {
            Logger.LogError(
                "No Dataverse solution project found at '{Path}'. To start a new project from an existing Dataverse solution, use: txc environment solution clone",
                dirPath);
#pragma warning disable TXC003
            OutputWriter.WriteLine($"Error: no .cdsproj or .csproj found in '{dirPath}'. To create a new project from an existing Dataverse solution, use: txc environment solution clone");
#pragma warning restore TXC003
            return null;
        }

        var resolvedRoot = ResolveSolutionRoot(dirPath, projectFile);
        if (resolvedRoot is null)
            return null;

        var uniqueName = SolutionProjectResolver.ReadSolutionUniqueName(resolvedRoot);
        if (string.IsNullOrWhiteSpace(uniqueName))
        {
            Logger.LogError("Could not read <UniqueName> from '{Path}'.", Path.Combine(resolvedRoot, "Other", "Solution.xml"));
            return null;
        }

        Logger.LogInformation("Resolved solution '{UniqueName}' from project directory.", uniqueName);
        return (uniqueName, resolvedRoot, projectFile);
    }

    private string? ResolveSolutionRoot(string dirPath, string projectFile)
    {
        if (Output is not null)
            return Path.GetFullPath(Output);

        var declared = SolutionProjectResolver.ReadSolutionRootPath(projectFile);
        if (string.IsNullOrWhiteSpace(declared))
            return dirPath;

        var resolved = SolutionProjectResolver.ResolveSolutionRoot(projectFile);
        if (resolved is null)
            Logger.LogError("Solution root path '{SolutionRootPath}' (from project) does not exist.", declared);
        return resolved;
    }

    private static bool IsDirectoryPath(string value)
    {
        if (value == ".")
            return true;
        if (value.Contains('/') || value.Contains('\\'))
            return true;
        if (Directory.Exists(value))
            return true;
        return false;
    }
}
