using System.Globalization;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Data;

/// <summary>
/// Reads and writes the connected user's <c>usersettings</c> record and looks
/// up <c>timezonedefinition</c> rows via the <c>ServiceClient</c> SDK.
/// </summary>
internal sealed class UserSettingsService : IUserSettingsService
{
    public async Task<UserSettingsInfo> GetCurrentAsync(string? profileName, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        var userId = await WhoAmIAsync(conn, ct).ConfigureAwait(false);

        var settings = await conn.Client.RetrieveAsync(
            "usersettings", userId,
            new ColumnSet("timezonecode", "localeid", "dateformatcode", "dateformatstring", "longdateformatcode", "timeformatstring", "transactioncurrencyid"),
            ct).ConfigureAwait(false);

        var user = await conn.Client.RetrieveAsync(
            "systemuser", userId,
            new ColumnSet("fullname", "internalemailaddress"),
            ct).ConfigureAwait(false);

        var timezoneCode = settings.GetAttributeValue<int?>("timezonecode");
        var localeId = settings.GetAttributeValue<int?>("localeid");
        var currency = settings.GetAttributeValue<EntityReference?>("transactioncurrencyid") is { } currencyRef
            ? await TryGetCurrencyAsync(conn, currencyRef.Id, ct).ConfigureAwait(false)
            : null;

        return new UserSettingsInfo(
            UserId: userId,
            FullName: user.GetAttributeValue<string?>("fullname"),
            Email: user.GetAttributeValue<string?>("internalemailaddress"),
            TimeZoneCode: timezoneCode,
            TimeZoneName: timezoneCode is { } code ? await TryGetTimezoneNameAsync(conn, code, ct).ConfigureAwait(false) : null,
            LocaleId: localeId,
            LocaleName: LocaleName(localeId),
            ShortDateFormatCode: settings.GetAttributeValue<int?>("dateformatcode"),
            ShortDateFormat: settings.GetAttributeValue<string?>("dateformatstring"),
            LongDateFormatCode: settings.GetAttributeValue<int?>("longdateformatcode"),
            TimeFormat: settings.GetAttributeValue<string?>("timeformatstring"),
            CurrencyCode: currency?.IsoCode,
            CurrencyName: currency?.Name);
    }

    public async Task UpdateCurrentAsync(string? profileName, UserSettingsUpdate update, CancellationToken ct)
    {
        if (update.IsEmpty)
            throw new ArgumentException("No settings to update.");

        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        var userId = await WhoAmIAsync(conn, ct).ConfigureAwait(false);

        var entity = new Entity("usersettings", userId);
        
        if (update.TimeZoneCode is { } timeZoneCode) entity["timezonecode"] = timeZoneCode;
        if (update.LocaleId is { } localeId) entity["localeid"] = localeId;
        if (update.ShortDateFormatCode is { } shortDateCode) entity["dateformatcode"] = shortDateCode;
        if (update.LongDateFormatCode is { } longDateCode) entity["longdateformatcode"] = longDateCode;
        if (update.TimeFormat is { } timeFormat) entity["timeformatstring"] = timeFormat;
        if (update.CurrencyId is { } currencyId) entity["transactioncurrencyid"] = new EntityReference("transactioncurrency", currencyId);

        await conn.Client.UpdateAsync(entity, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TimezoneInfo>> ListTimezonesAsync(string? profileName, string? filter, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        var all = await QueryTimezonesAsync(conn, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(filter))
            return all;

        return all
            .Where(timezone => timezone.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                     || (timezone.StandardName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
    }

    public async Task<int> ResolveTimezoneCodeAsync(string? profileName, string query, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        var all = await QueryTimezonesAsync(conn, ct).ConfigureAwait(false);
        return TimezoneResolver.Resolve(all, query).Code;
    }

    public async Task<IReadOnlyList<CurrencyInfo>> ListCurrenciesAsync(string? profileName, string? filter, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        var all = await QueryCurrenciesAsync(conn, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(filter))
            return all;

        return all
            .Where(currency => currency.IsoCode.Contains(filter, StringComparison.OrdinalIgnoreCase)
                     || currency.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task<Guid> ResolveCurrencyIdAsync(string? profileName, string query, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        var all = await QueryCurrenciesAsync(conn, ct).ConfigureAwait(false);
        return CurrencyResolver.Resolve(all, query).Id;
    }

    private static async Task<Guid> WhoAmIAsync(DataverseConnection conn, CancellationToken ct)
    {
        var response = (WhoAmIResponse)await conn.Client.ExecuteAsync(new WhoAmIRequest(), ct).ConfigureAwait(false);
        return response.UserId;
    }

    private static async Task<IReadOnlyList<TimezoneInfo>> QueryTimezonesAsync(DataverseConnection conn, CancellationToken ct)
    {
        var query = new QueryExpression("timezonedefinition")
        {
            ColumnSet = new ColumnSet("timezonecode", "userinterfacename", "standardname"),
            Orders = { new OrderExpression("userinterfacename", OrderType.Ascending) }
        };

        var result = await conn.Client.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return result.Entities
            .Select(entity => new TimezoneInfo(
                entity.GetAttributeValue<int>("timezonecode"),
                entity.GetAttributeValue<string?>("userinterfacename") ?? string.Empty,
                entity.GetAttributeValue<string?>("standardname")))
            .ToList();
    }

    private static async Task<string?> TryGetTimezoneNameAsync(DataverseConnection conn, int code, CancellationToken ct)
    {
        var query = new QueryExpression("timezonedefinition")
        {
            ColumnSet = new ColumnSet("userinterfacename"),
            Criteria = { Conditions = { new ConditionExpression("timezonecode", ConditionOperator.Equal, code) } },
            TopCount = 1
        };

        var result = await conn.Client.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return result.Entities.Count > 0
            ? result.Entities[0].GetAttributeValue<string?>("userinterfacename")
            : null;
    }

    private static async Task<IReadOnlyList<CurrencyInfo>> QueryCurrenciesAsync(DataverseConnection conn, CancellationToken ct)
    {
        var query = new QueryExpression("transactioncurrency")
        {
            ColumnSet = new ColumnSet("transactioncurrencyid", "isocurrencycode", "currencyname", "currencysymbol"),
            Orders = { new OrderExpression("isocurrencycode", OrderType.Ascending) }
        };

        var result = await conn.Client.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return result.Entities
            .Select(entity => new CurrencyInfo(
                entity.Id,
                entity.GetAttributeValue<string?>("isocurrencycode") ?? string.Empty,
                entity.GetAttributeValue<string?>("currencyname") ?? string.Empty,
                entity.GetAttributeValue<string?>("currencysymbol")))
            .ToList();
    }

    private static async Task<CurrencyInfo?> TryGetCurrencyAsync(DataverseConnection conn, Guid id, CancellationToken ct)
    {
        var currency = await conn.Client.RetrieveAsync(
            "transactioncurrency", id,
            new ColumnSet("isocurrencycode", "currencyname", "currencysymbol"),
            ct).ConfigureAwait(false);

        return new CurrencyInfo(
            id,
            currency.GetAttributeValue<string?>("isocurrencycode") ?? string.Empty,
            currency.GetAttributeValue<string?>("currencyname") ?? string.Empty,
            currency.GetAttributeValue<string?>("currencysymbol"));
    }

    private static string? LocaleName(int? localeId)
    {
        if (localeId is null)
            return null;
        try
        {
            return CultureInfo.GetCultureInfo(localeId.Value).Name;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}
