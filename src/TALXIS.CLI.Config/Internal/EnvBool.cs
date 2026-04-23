namespace TALXIS.CLI.Config.Internal;

/// <summary>
/// Shared parser for truthy env-var values (<c>1</c> / <c>true</c> / <c>yes</c>,
/// case-insensitive). Consolidates duplicates previously in
/// <c>HeadlessDetector</c> and <c>VaultOptions</c>.
/// </summary>
internal static class EnvBool
{
    public static bool IsTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.Equals("1", StringComparison.Ordinal)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}
