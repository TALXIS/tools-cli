using Microsoft.Xrm.Sdk;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

/// <summary>
/// Resolves localized labels from Dataverse metadata, supporting specific language codes.
/// </summary>
internal static class LabelHelper
{
    /// <summary>
    /// Gets the label text for a specific language, falling back to <see cref="Label.UserLocalizedLabel"/>.
    /// </summary>
    /// <param name="label">The Dataverse label containing localized strings.</param>
    /// <param name="languageCode">Optional LCID (e.g. 1033 for English, 1029 for Czech). Null = user's language.</param>
    internal static string? GetLabel(Label? label, int? languageCode = null)
    {
        if (label is null) return null;

        if (languageCode.HasValue && label.LocalizedLabels is { Count: > 0 })
        {
            var match = label.LocalizedLabels
                .FirstOrDefault(l => l.LanguageCode == languageCode.Value);
            if (match?.Label is not null)
                return match.Label;
        }

        return label.UserLocalizedLabel?.Label;
    }
}
