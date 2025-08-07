using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace TALXIS.CLI.DataVisualizer.Extensions;

public static class StringExtension
{
    public static string FirstCharToUpper(this string input) => input switch
    {
        null => throw new ArgumentNullException(nameof(input)),
        "" => throw new ArgumentException($"{nameof(input)} cannot be empty", nameof(input)),
        _ => string.Concat(input.First().ToString().ToUpper(), input.AsSpan(1))
    };

    /// <summary>
    /// Remove diacritics, pascal case, remove spaces and replace "-" with "_"
    /// </summary>
    /// <param name="text"></param>
    /// <returns></returns>
    public static string NormalizeString(this string text)
    {
        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder();

        foreach (var c in normalizedString)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(stringBuilder.ToString().Normalize(NormalizationForm.FormC)).Replace(" ", "").Replace("-", "_");

    }
}
