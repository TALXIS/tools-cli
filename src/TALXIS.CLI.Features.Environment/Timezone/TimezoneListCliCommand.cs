using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Timezone;

/// <summary>
/// <c>txc environment timezone list</c> - lists available timezones and their
/// codes, optionally filtered by name.
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "Lists Dataverse timezones (code + name) from the LIVE connected environment. Requires an active profile."
)]
public class TimezoneListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(TimezoneListCliCommand));

    [CliOption(Name = "--filter", Description = "Show only timezones whose name contains this substring.", Required = false)]
    public string? Filter { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IUserSettingsService>();
        var timezones = await service.ListTimezonesAsync(Profile, Filter, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteList(timezones, PrintTable);
        return ExitSuccess;
    }

    // Text-renderer callback invoked by OutputFormatter.WriteList; OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintTable(IReadOnlyList<TimezoneInfo> timezones)
    {
        if (timezones.Count == 0)
        {
            OutputWriter.WriteLine("No timezones found.");
            return;
        }

        OutputWriter.WriteLine($"{"Code",-5} | Name");
        OutputWriter.WriteLine(new string('-', 60));
        foreach (var timezone in timezones)
        {
            OutputWriter.WriteLine($"{timezone.Code,-5} | {timezone.Name}");
        }
    }
#pragma warning restore TXC003
}
