using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Resolution;
using TALXIS.CLI.Core.Shared;
using TALXIS.CLI.Features.Workspace.TemplateEngine;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Solution;

/// <summary>
/// Scaffolds a TALXIS Dataverse solution project from an existing Dataverse solution and
/// populates it with the current solution state from the environment.
/// This is the first-time "bring it down to the repo" command.
/// For updating an already-scaffolded project, use: txc environment solution pull
/// </summary>
[CliIdempotent]
[CliLongRunning]
[CliCommand(
    Name = "clone",
    Description = "Clone a Dataverse solution from the LIVE environment into a new local project: scaffolds the TALXIS solution project structure and pulls the current solution state. For updating an existing project, use: txc environment solution pull. Requires an active profile."
)]
public class SolutionCloneCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(SolutionCloneCliCommand));

    [CliArgument(Name = "name", Description = "Solution unique name to clone from Dataverse.")]
    public string Name { get; set; } = null!;

    [CliOption(Name = "--output", Alias = "-o", Description = "Directory where the new solution project will be scaffolded. Required.", Required = true)]
    public string Output { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
    {
        var outputPath = Path.GetFullPath(Output);

        // Guard: refuse to scaffold into a directory that already has a solution project.
        if (Directory.Exists(outputPath))
        {
            var hasProject = SolutionProjectResolver.FindProjectFile(outputPath) is not null;
            var hasSolutionXml = File.Exists(Path.Combine(outputPath, "Other", "Solution.xml"));
            if (hasProject || hasSolutionXml)
            {
                Logger.LogError(
                    "A Dataverse solution project already exists at '{Path}'. To update it, use: txc environment solution pull",
                    outputPath);
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Error: project already exists at '{outputPath}'. To update it, use: txc environment solution pull");
#pragma warning restore TXC003
                return ExitValidationError;
            }
        }

        // Fetch publisher info from Dataverse so the template is seeded correctly.
        Logger.LogInformation("Fetching solution details for '{SolutionName}'...", Name);
        var detailService = TxcServices.Get<ISolutionDetailService>();
        (SolutionDetail solution, _) = await detailService.ShowAsync(Profile, Name, CancellationToken.None).ConfigureAwait(false);

        var publisherName = solution.PublisherName;
        var publisherPrefix = solution.PublisherPrefix;

        if (string.IsNullOrWhiteSpace(publisherName) || string.IsNullOrWhiteSpace(publisherPrefix))
        {
            Logger.LogError(
                "Could not read publisher name or prefix from solution '{SolutionName}' in Dataverse. Ensure the solution exists and you have access.",
                Name);
            return ExitError;
        }

        // Scaffold the project using the installed pp-solution dotnet template.
        // The template owns the SDK version — we do not hardcode it here.
        Logger.LogInformation(
            "Scaffolding solution project at '{OutputPath}' (PublisherName={PublisherName}, PublisherPrefix={PublisherPrefix})...",
            outputPath, publisherName, publisherPrefix);

        var prereqProblems = PrerequisiteChecker.CheckScaffoldingPrerequisites();
        foreach (var problem in prereqProblems)
            Logger.LogError("{Problem}", problem);
        if (prereqProblems.Count > 0)
            return ExitError;

        using var scaffolder = new TemplateInvoker();
        var (scaffoldSuccess, failedActions, failedActionErrors) = await scaffolder.ScaffoldAsync(
            "pp-solution",
            outputPath,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PublisherName"] = publisherName,
                ["PublisherPrefix"] = publisherPrefix,
                // SolutionRootPath=. means the project folder IS the solution root (TALXIS SDK convention).
                // The pull pipeline will merge Dataverse's Other/Solution.xml directly into outputPath.
                ["SolutionRootPath"] = ".",
            }).ConfigureAwait(false);

        if (!scaffoldSuccess || failedActions.Count > 0)
        {
            foreach (var failed in failedActions)
            {
                var label = !string.IsNullOrWhiteSpace(failed.Description) ? failed.Description : failed.ActionId.ToString();
                Logger.LogError("Scaffold post-action failed: {Label}", label);
            }
            Logger.LogError("Solution project scaffolding failed — no files were written.");
            return ExitError;
        }

        Logger.LogInformation("Scaffold succeeded. Running pull to populate solution files from Dataverse...");

        // Run the pull pipeline to populate the newly scaffolded project.
        var projectFile = SolutionProjectResolver.FindProjectFile(outputPath);
        var solutionRoot = SolutionProjectResolver.ResolveSolutionRoot(projectFile ?? outputPath);
        solutionRoot ??= outputPath; // SolutionRootPath=. means project dir is the root

        var pullOptions = new SolutionPullOptions(Name, solutionRoot, projectFile);
        var pullService = TxcServices.Get<ISolutionPullService>();
        var result = await pullService.PullAsync(Profile, pullOptions, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteData(
            new
            {
                status = "cloned",
                solution = Name,
                path = outputPath,
                normalizedAssemblies = result.NormalizedAssemblies,
                excludedBinaries = result.ExcludedBinaries,
                excludedWebResources = result.ExcludedWebResources,
                excludedPcfControls = result.ExcludedPcfControls,
                removedFiles = result.RemovedFiles,
            },
            _ =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Cloned solution '{Name}' → {outputPath}");
                if (result.NormalizedAssemblies.Count > 0)
                    OutputWriter.WriteLine($"  Restored {result.NormalizedAssemblies.Count} plugin assembly path(s)");
                if (result.ExcludedBinaries.Count > 0)
                    OutputWriter.WriteLine($"  Excluded {result.ExcludedBinaries.Count} project-reference binary(ies)");
                if (result.ExcludedWebResources.Count > 0)
                    OutputWriter.WriteLine($"  Excluded {result.ExcludedWebResources.Count} script-library web resource(s)");
                if (result.ExcludedPcfControls.Count > 0)
                    OutputWriter.WriteLine($"  Excluded {result.ExcludedPcfControls.Count} PCF control(s)");
#pragma warning restore TXC003
            });

        return ExitSuccess;
    }
}
