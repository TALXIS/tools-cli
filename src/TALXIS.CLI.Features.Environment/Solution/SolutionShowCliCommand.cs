using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Solution;

[CliReadOnly]
[CliCommand(
    Name = "show",
    Description = "Show detailed information about an installed solution."
)]
public class SolutionShowCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(SolutionShowCliCommand));

    [CliArgument(Name = "name", Description = "Solution unique name.")]
    public string Name { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<ISolutionDetailService>();
        var (solution, counts) = await service.ShowAsync(Profile, Name, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteData(
            new { solution, counts },
            _ => PrintSolutionDetail(solution, counts));

        return ExitSuccess;
    }

    // Text-renderer callback — OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintSolutionDetail(SolutionDetail s, IReadOnlyList<ComponentCountRow> counts)
    {
        OutputWriter.WriteLine($"Solution:    {s.UniqueName}");
        OutputWriter.WriteLine($"Display:     {s.FriendlyName ?? "(none)"}");
        OutputWriter.WriteLine($"Version:     {s.Version ?? "(unknown)"}");
        OutputWriter.WriteLine($"Managed:     {(s.Managed ? "true" : "false")}");
        OutputWriter.WriteLine($"Publisher:   {s.PublisherName ?? "(unknown)"} (prefix: {s.PublisherPrefix ?? "n/a"})");
        if (s.InstalledOn.HasValue)
            OutputWriter.WriteLine($"Installed:   {s.InstalledOn.Value:yyyy-MM-dd HH:mm:ss}");
        if (!string.IsNullOrWhiteSpace(s.Description))
            OutputWriter.WriteLine($"Description: {s.Description}");

        if (counts.Count > 0)
        {
            OutputWriter.WriteLine();
            OutputWriter.WriteLine("Component Types:");
            int nameWidth = Math.Clamp(counts.Max(c => c.TypeName.Length), 15, 40);
            foreach (var c in counts)
            {
                OutputWriter.WriteLine($"  {c.TypeName.PadRight(nameWidth)}  {c.Count,5}");
            }
        }
    }
#pragma warning restore TXC003
}
