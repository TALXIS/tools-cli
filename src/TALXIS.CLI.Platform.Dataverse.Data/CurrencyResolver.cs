using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Platform.Dataverse.Data;

/// <summary>
/// Pure name/ISO-code-to-currency matching so <c>user-settings set --currency</c>
/// can take "CZK" or "Czech Koruna" instead of a record id.
/// </summary>
internal static class CurrencyResolver
{
    /// <summary>
    /// Finds the single currency matching <paramref name="query"/>. An exact
    /// (case-insensitive) ISO code wins; otherwise a substring match on the
    /// code, name, or symbol must be unique. Throws <see cref="ArgumentException"/>
    /// on no match or ambiguity.
    /// </summary>
    public static CurrencyInfo Resolve(IReadOnlyList<CurrencyInfo> currencies, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Currency query cannot be empty.");

        var trimmedQuery = query.Trim();

        var exactMatches = currencies
            .Where(currency => string.Equals(currency.IsoCode, trimmedQuery, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (exactMatches.Count == 1)
            return exactMatches[0];

        var matches = currencies
            .Where(currency => currency.IsoCode.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase)
                     || currency.Name.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase)
                     || (currency.Symbol?.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

        if (matches.Count == 1)
            return matches[0];

        if (matches.Count == 0)
            throw new ArgumentException(
                $"No currency matches '{query}'. Run 'txc env currency list --filter {query}' to browse.");

        var sample = string.Join(Environment.NewLine, matches.Take(10).Select(currency => $"  {currency.IsoCode,-5} {currency.Name}"));
        throw new ArgumentException(
            $"'{query}' matches {matches.Count} currencies. Use the exact ISO code:{Environment.NewLine}{sample}");
    }
}
