using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Solution.Component;

[CliReadOnly]
[CliCommand(
    Name = "count",
    Description = "Show component counts per type in a solution."
)]
public class SolutionComponentCountCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(SolutionComponentCountCliCommand));

    [CliArgument(Name = "solution", Description = "Solution unique name.")]
    public string SolutionName { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<ISolutionComponentQueryService>();
        var counts = await service.CountAsync(Profile, SolutionName, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteList(counts, PrintCountTable);
        return ExitSuccess;
    }

    // Text-renderer callback — OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintCountTable(IReadOnlyList<ComponentCountRow> counts)
    {
        if (counts.Count == 0)
        {
            OutputWriter.WriteLine("No components in this solution.");
            return;
        }

        int nameWidth = Math.Clamp(counts.Max(c => c.TypeName.Length), 15, 40);
        string header = $"{"Type".PadRight(nameWidth)} | {"Code",5} | {"Count",6}";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));
        foreach (var c in counts)
        {
            OutputWriter.WriteLine($"{c.TypeName.PadRight(nameWidth)} | {c.TypeCode,5} | {c.Count,6}");
        }
        OutputWriter.WriteLine($"\nTotal: {counts.Sum(c => c.Count)} component(s) across {counts.Count} type(s).");
    }
#pragma warning restore TXC003
}
