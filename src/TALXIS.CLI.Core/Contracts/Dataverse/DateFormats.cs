namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Maps Dataverse date format strings to their codes
/// </summary>
public static class DateFormats
{
    /// <summary>Short date formats mapped to their <c>dateformatcode</c>.</summary>
    public static readonly IReadOnlyDictionary<string, int> Short = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["dd/MMMM/yy"] = 0,
        ["M/d/yy"] = 1,
        ["M/d/yyyy"] = 2,
        ["MM/dd/yy"] = 3,
        ["MM/dd/yyyy"] = 4,
        ["yy/MM/dd"] = 5,
        ["yyyy/MM/dd"] = 6,
    };

    /// <summary>Long date formats mapped to their <c>longdateformatcode</c>.</summary>
    public static readonly IReadOnlyDictionary<string, int> Long = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["d MMMM, yyyy"] = 0,
        ["dddd, d MMMM, yyyy"] = 1,
        ["dddd, MMMM d, yyyy"] = 2,
        ["MMMM d, yyyy"] = 3,
    };

    /// <summary>
    /// Turns user input into a format code. 
    /// </summary>
    public static int ToCode(IReadOnlyDictionary<string, int> formats, string input, string label)
    {
        if (string.IsNullOrWhiteSpace(input)) throw new ArgumentException($"{label} cannot be empty.");
            

        if (int.TryParse(input, out var code)) return code;
            

        if (formats.TryGetValue(input, out var mapped)) return mapped;
            
        var known = string.Join(Environment.NewLine, formats.OrderBy(pair => pair.Value).Select(pair => $"  {pair.Value}  {pair.Key}"));
        throw new ArgumentException(
            $"'{input}' is not a known {label}. Pass a code or one of:{Environment.NewLine}{known}");
    }

    /// <summary>
    /// Returns the format string for a code, or null when the code is unknown.
    /// </summary>
    public static string? Describe(IReadOnlyDictionary<string, int> formats, int? code) => code is { } codeValue ? formats.FirstOrDefault(pair => pair.Value == codeValue).Key : null;
}
