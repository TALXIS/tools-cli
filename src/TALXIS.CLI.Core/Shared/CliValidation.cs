namespace TALXIS.CLI.Core;

/// <summary>
/// Shared validation constants for CLI argument and option attributes.
/// Values are <see langword="const"/> so they can be used in
/// <c>[CliArgument]</c> / <c>[CliOption]</c> attribute declarations.
/// </summary>
public static class CliValidation
{
    /// <summary>
    /// Regex pattern that accepts all common GUID text representations:
    /// hyphenated (<c>00000000-0000-0000-0000-000000000000</c>),
    /// braces (<c>{00000000-0000-0000-0000-000000000000}</c>), and
    /// bare 32-character hex (<c>00000000000000000000000000000000</c>).
    /// Matches the formats accepted by <see cref="System.Guid.TryParse"/>.
    /// </summary>
    public const string GuidPattern =
        @"^(\{?[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}?|[0-9a-fA-F]{32})$";

    /// <summary>
    /// User-facing validation error message displayed when a GUID argument
    /// does not match <see cref="GuidPattern"/>.
    /// </summary>
    public const string GuidValidationMessage =
        "Value must be a valid GUID (e.g. 00000000-0000-0000-0000-000000000000).";
}
