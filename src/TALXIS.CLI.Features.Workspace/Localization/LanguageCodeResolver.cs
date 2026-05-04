using System.Globalization;

namespace TALXIS.CLI.Features.Workspace.Localization;

public static class LanguageCodeResolver
{
    public static string Resolve(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Language code must be provided.", nameof(input));

        var trimmed = input.Trim();

        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lcid))
            return lcid.ToString(CultureInfo.InvariantCulture);

        try
        {
            var culture = CultureInfo.GetCultureInfo(trimmed);
            return culture.LCID.ToString(CultureInfo.InvariantCulture);
        }
        catch (CultureNotFoundException)
        {
            throw new ArgumentException($"Unknown language '{input}'. Use a locale (e.g. cs-CZ) or LCID number (e.g. 1029).", nameof(input));
        }
    }

    public static string ToLocale(string lcid)
    {
        if (int.TryParse(lcid, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            try
            {
                return CultureInfo.GetCultureInfo(parsed).Name;
            }
            catch (CultureNotFoundException)
            {
                return lcid;
            }
        }
        return lcid;
    }
}
