using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.UserSettings;

/// <summary>
/// <c>txc environment user-settings get</c> - shows the connected user's
/// timezone, locale, date/time formats, and currency.
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "get",
    Description = "Shows the connected user's settings (timezone, locale, date/time formats, currency) from the LIVE connected environment. Requires an active profile."
)]
public class UserSettingsGetCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(UserSettingsGetCliCommand));

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IUserSettingsService>();
        var settings = await service.GetCurrentAsync(Profile, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteData(settings, Print);
        return ExitSuccess;
    }

    // Text-renderer callback invoked by OutputFormatter.WriteData; OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void Print(UserSettingsInfo userSettingsInfo)
    {
        var user = userSettingsInfo.FullName is { } name
            ? (userSettingsInfo.Email is { } email ? $"{name} ({email})" : name)
            : userSettingsInfo.UserId.ToString();
        var timezone = userSettingsInfo.TimeZoneCode is { } code
            ? (userSettingsInfo.TimeZoneName is { } tzName ? $"{tzName} (code: {code})" : $"code {code}")
            : "(not set)";
        var locale = userSettingsInfo.LocaleId is { } localeId
            ? (userSettingsInfo.LocaleName is { } localeName ? $"{localeName} (code: {localeId})" : $"code {localeId}")
            : "(not set)";
        var currency = userSettingsInfo.CurrencyCode is { } isoCode
            ? (userSettingsInfo.CurrencyName is { } currencyName ? $"{isoCode} ({currencyName})" : isoCode)
            : "(not set)";
        var shortDate = FormatCode(userSettingsInfo.ShortDateFormatCode, userSettingsInfo.ShortDateFormat ?? DateFormats.Describe(DateFormats.Short, userSettingsInfo.ShortDateFormatCode));
        var longDate = FormatCode(userSettingsInfo.LongDateFormatCode, DateFormats.Describe(DateFormats.Long, userSettingsInfo.LongDateFormatCode));

        OutputWriter.WriteLine($"User:              {user}");
        OutputWriter.WriteLine($"Timezone:          {timezone}");
        OutputWriter.WriteLine($"Locale:            {locale}");
        OutputWriter.WriteLine($"Short date:        {shortDate}");
        OutputWriter.WriteLine($"Long date:         {longDate}");
        OutputWriter.WriteLine($"Time format:       {userSettingsInfo.TimeFormat ?? "(not set)"}");
        OutputWriter.WriteLine($"Currency:          {currency}");
    }

    private static string FormatCode(int? code, string? description)
    {
        if (code is not { } codeValue)
            return "(not set)";
        return description is { } format ? $"{format} (code: {codeValue})" : codeValue.ToString();
    }
#pragma warning restore TXC003
}
