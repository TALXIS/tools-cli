using System.Globalization;

namespace TALXIS.CLI.Config.Providers.Dataverse.Platforms;

/// <summary>
/// Parses compact relative-time tokens used by <c>txc environment deployment list --since</c>.
/// Accepted suffixes: <c>m</c> (minutes), <c>h</c> (hours), <c>d</c> (days), <c>w</c> (weeks).
/// The numeric portion must be a positive integer.
/// </summary>
public static class DeploymentRelativeTimeParser
{
    /// <summary>
    /// Parses <paramref name="value"/> into a <see cref="TimeSpan"/>. Returns <c>false</c>
    /// on any malformed or non-positive input.
    /// </summary>
    public static bool TryParse(string? value, out TimeSpan result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Length < 2)
        {
            return false;
        }

        char suffix = char.ToLowerInvariant(trimmed[^1]);
        var numberPart = trimmed[..^1];

        if (!int.TryParse(numberPart, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            return false;
        }

        result = suffix switch
        {
            'm' => TimeSpan.FromMinutes(amount),
            'h' => TimeSpan.FromHours(amount),
            'd' => TimeSpan.FromDays(amount),
            'w' => TimeSpan.FromDays(amount * 7),
            _ => TimeSpan.Zero,
        };

        return result > TimeSpan.Zero;
    }
}
