using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Publisher;

[CliReadOnly]
[CliCommand(Name = "list", Description = "List solution publishers in the target environment.")]
public class PublisherListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(PublisherListCliCommand));

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IPublisherService>();
        var rows = await service.ListAsync(Profile, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteList(rows, PrintTable);
        return ExitSuccess;
    }

#pragma warning disable TXC003
    private static void PrintTable(IReadOnlyList<PublisherRecord> rows)
    {
        if (rows.Count == 0) { OutputWriter.WriteLine("No publishers found."); return; }

        int nameWidth = Math.Clamp(rows.Max(r => r.UniqueName.Length), 15, 40);
        int prefixWidth = 8;
        string header = $"{"Unique Name".PadRight(nameWidth)} | {"Prefix".PadRight(prefixWidth)} | Friendly Name";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));
        foreach (var r in rows)
        {
            OutputWriter.WriteLine($"{r.UniqueName.PadRight(nameWidth)} | {(r.CustomizationPrefix ?? "").PadRight(prefixWidth)} | {r.FriendlyName ?? "(none)"}");
        }
    }
#pragma warning restore TXC003
}
