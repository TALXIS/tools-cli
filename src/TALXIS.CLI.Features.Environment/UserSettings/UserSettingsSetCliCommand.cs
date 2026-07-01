using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.UserSettings;

/// <summary>
/// <c>txc environment user-settings set</c> - updates one or more of the
/// connected user's settings. Timezone and currency accept a name (fuzzy) or
/// their code/ISO code.
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "set",
    Description = "Updates the connected user's settings (timezone, locale, date/time formats, currency) on the LIVE connected environment. Requires an active profile. Pass at least one setting."
)]
public class UserSettingsSetCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(UserSettingsSetCliCommand));

    [CliOption(Name = "--timezone", Description = "Timezone by numeric code (e.g. 95) or by city/region name, fuzzy-matched (e.g. 'Prague').", Required = false)]
    public string? Timezone { get; set; }

    [CliOption(Name = "--locale", Description = "Locale ID / LCID (e.g. 1033 for en-US, 1029 for cs-CZ).", Required = false)]
    public int? Locale { get; set; }

    [CliOption(Name = "--short-date-format", Description = "Short date format string (e.g. 'M/d/yyyy') or its numeric code. See options via 'user-settings get'.", Required = false)]
    public string? ShortDateFormat { get; set; }

    [CliOption(Name = "--long-date-format", Description = "Long date format string (e.g. 'dddd, MMMM d, yyyy') or its numeric code. See options via 'user-settings get'.", Required = false)]
    public string? LongDateFormat { get; set; }

    [CliOption(Name = "--time-format", Description = "Time format string (e.g. 'H:mm', 'h:mm tt').", Required = false)]
    public string? TimeFormat { get; set; }

    [CliOption(Name = "--currency", Description = "Base currency by ISO code (e.g. 'CZK') or name, fuzzy-matched.", Required = false)]
    public string? Currency { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (Timezone is null && Locale is null && ShortDateFormat is null && LongDateFormat is null && TimeFormat is null && Currency is null)
        {
            Logger.LogError("Nothing to update. Pass at least one of --timezone, --locale, --short-date-format, --long-date-format, --time-format, --currency.");
            return ExitValidationError;
        }

        var service = TxcServices.Get<IUserSettingsService>();

        int? timezoneCode = null;
        if (Timezone is { } timezone)
        {
            timezoneCode = int.TryParse(timezone, out var code)
                ? code
                : await service.ResolveTimezoneCodeAsync(Profile, timezone, CancellationToken.None).ConfigureAwait(false);
        }

        Guid? currencyId = null;
        if (Currency is { } currency)
            currencyId = await service.ResolveCurrencyIdAsync(Profile, currency, CancellationToken.None).ConfigureAwait(false);

        int? shortDateCode = ShortDateFormat is { } shortDateFormat
            ? DateFormats.ToCode(DateFormats.Short, shortDateFormat, "short date format")
            : null;
        int? longDateCode = LongDateFormat is { } longDateFormat
            ? DateFormats.ToCode(DateFormats.Long, longDateFormat, "long date format")
            : null;

        var update = new UserSettingsUpdate(
            TimeZoneCode: timezoneCode,
            LocaleId: Locale,
            ShortDateFormatCode: shortDateCode,
            LongDateFormatCode: longDateCode,
            TimeFormat: TimeFormat,
            CurrencyId: currencyId);

        await service.UpdateCurrentAsync(Profile, update, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteResult("succeeded", "User settings updated.");
        return ExitSuccess;
    }
}
