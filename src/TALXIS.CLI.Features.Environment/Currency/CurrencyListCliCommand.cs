using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Currency;

/// <summary>
/// <c>txc environment currency list</c> - lists currencies enabled in the
/// environment (ISO code + name), optionally filtered.
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "Lists currencies enabled in the LIVE connected environment (ISO code + name). Requires an active profile."
)]
public class CurrencyListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(CurrencyListCliCommand));

    [CliOption(Name = "--filter", Description = "Show only currencies whose ISO code or name contains this substring.", Required = false)]
    public string? Filter { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IUserSettingsService>();
        var currencies = await service.ListCurrenciesAsync(Profile, Filter, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteList(currencies, PrintTable);
        return ExitSuccess;
    }

    // Text-renderer callback invoked by OutputFormatter.WriteList; OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintTable(IReadOnlyList<CurrencyInfo> currencies)
    {
        if (currencies.Count == 0)
        {
            OutputWriter.WriteLine("No currencies found.");
            return;
        }

        OutputWriter.WriteLine($"{"ISO",-5} | Name");
        OutputWriter.WriteLine(new string('-', 60));
        foreach (var currency in currencies)
        {
            OutputWriter.WriteLine($"{currency.IsoCode,-5} | {currency.Name}");
        }
    }
#pragma warning restore TXC003
}
