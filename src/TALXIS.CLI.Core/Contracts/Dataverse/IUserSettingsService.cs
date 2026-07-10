namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// The connected user's personalization settings (timezone, locale, date/time
/// formats, currency) read from the Dataverse <c>usersettings</c> entity.
/// </summary>
public sealed record UserSettingsInfo(
    Guid UserId,
    string? FullName,
    string? Email,
    int? TimeZoneCode,
    string? TimeZoneName,
    int? LocaleId,
    string? LocaleName,
    int? ShortDateFormatCode,
    string? ShortDateFormat,
    int? LongDateFormatCode,
    string? TimeFormat,
    string? CurrencyCode,
    string? CurrencyName);

/// <summary>A Dataverse <c>timezonedefinition</c> row.</summary>
public sealed record TimezoneInfo(int Code, string Name, string? StandardName);

/// <summary>A Dataverse <c>transactioncurrency</c> row.</summary>
public sealed record CurrencyInfo(Guid Id, string IsoCode, string Name, string? Symbol);

/// <summary>
/// The subset of user settings to change. Null fields are left untouched.
/// </summary>
public sealed record UserSettingsUpdate(
    int? TimeZoneCode = null,
    int? LocaleId = null,
    int? ShortDateFormatCode = null,
    int? LongDateFormatCode = null,
    string? TimeFormat = null,
    Guid? CurrencyId = null)
{
    public bool IsEmpty => TimeZoneCode is null && LocaleId is null && ShortDateFormatCode is null
        && LongDateFormatCode is null && TimeFormat is null && CurrencyId is null;
}

/// <summary>
/// Reads and updates the connected user's personalization settings and looks
/// up available timezones and currencies. Keyed off the current user resolved
/// via <c>WhoAmI</c>, so it always targets the profile's own account.
/// </summary>
public interface IUserSettingsService
{
    /// <summary>Gets the connected user's current settings.</summary>
    Task<UserSettingsInfo> GetCurrentAsync(string? profileName, CancellationToken ct);

    /// <summary>Applies the non-null fields of <paramref name="update"/> to the connected user.</summary>
    Task UpdateCurrentAsync(string? profileName, UserSettingsUpdate update, CancellationToken ct);

    /// <summary>Lists timezones, optionally filtered by a substring of the display or standard name.</summary>
    Task<IReadOnlyList<TimezoneInfo>> ListTimezonesAsync(string? profileName, string? filter, CancellationToken ct);

    /// <summary>
    /// Resolves a timezone name (fuzzy) to its numeric code. Throws
    /// <see cref="ArgumentException"/> when nothing matches or the query is ambiguous.
    /// </summary>
    Task<int> ResolveTimezoneCodeAsync(string? profileName, string query, CancellationToken ct);

    /// <summary>Lists currencies enabled in the environment, optionally filtered by a substring of the ISO code or name.</summary>
    Task<IReadOnlyList<CurrencyInfo>> ListCurrenciesAsync(string? profileName, string? filter, CancellationToken ct);

    /// <summary>
    /// Resolves a currency (ISO code or name, fuzzy) to its record id. Throws
    /// <see cref="ArgumentException"/> when nothing matches or the query is ambiguous.
    /// </summary>
    Task<Guid> ResolveCurrencyIdAsync(string? profileName, string query, CancellationToken ct);
}
