using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Solution;

[CliReadOnly]
[CliCommand(
    Name = "uninstall-check",
    Description = "Check whether a solution can be safely uninstalled by listing blocking dependencies."
)]
public class SolutionUninstallCheckCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(SolutionUninstallCheckCliCommand));

    [CliArgument(Name = "name", Description = "Solution unique name.")]
    public string Name { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<ISolutionDependencyService>();
        var deps = await service.CheckUninstallAsync(Profile, Name, CancellationToken.None).ConfigureAwait(false);

        if (deps.Count == 0)
        {
            OutputFormatter.WriteData(
                new { status = "safe", solution = Name, blockingDependencies = 0 },
                _ => PrintSafe());
        }
        else
        {
            OutputFormatter.WriteData(
                new { status = "blocked", solution = Name, blockingDependencies = deps.Count, dependencies = deps },
                _ => PrintBlocked(deps));
            return ExitError;
        }

        return ExitSuccess;
    }

    // Text-renderer callbacks — OutputWriter usage is intentional.
#pragma warning disable TXC003
    private void PrintSafe()
    {
        OutputWriter.WriteLine($"Solution '{Name}' can be safely uninstalled. No blocking dependencies found.");
    }

    private void PrintBlocked(IReadOnlyList<DependencyRow> deps)
    {
        var resolver = new ComponentTypeResolver();
        OutputWriter.WriteLine($"Solution '{Name}' has {deps.Count} blocking dependency(ies):\n");

        string header = $"{"Required Type",-25} | {"Required ID",-36} | {"Dependent Type",-25} | {"Dependent ID",-36} | Dep.Type";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var d in deps)
        {
            var reqType = resolver.ResolveName(d.RequiredComponentType);
            var depType = resolver.ResolveName(d.DependentComponentType);
            var depKind = d.DependencyType switch
            {
                1 => "Published",
                2 => "Internal",
                4 => "Unpublished",
                _ => d.DependencyType.ToString(),
            };
            OutputWriter.WriteLine($"{reqType,-25} | {d.RequiredComponentId,-36} | {depType,-25} | {d.DependentComponentId,-36} | {depKind}");
        }

        OutputWriter.WriteLine($"\nUninstalling '{Name}' would break {deps.Count} dependent component(s). Resolve these dependencies first.");
    }
#pragma warning restore TXC003
}
