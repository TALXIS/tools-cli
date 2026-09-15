namespace TALXIS.CLI.Platform.PowerPlatform.Control.Graph;

/// <summary>
/// Shared Microsoft Graph OData <c>$filter</c> construction helpers used by
/// every <c>txc tenant</c> command that resolves a user or application by a
/// caller-supplied GUID-or-friendly-name identifier (e.g. <c>--user</c>,
/// <c>--service-principal</c>).
/// </summary>
public static class GraphODataFilterSupport
{
    public static string EscapeODataString(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    /// <summary>
    /// Builds an OData <c>$filter</c> combining GUID-typed property equality
    /// clauses with string-typed property equality clauses, for an
    /// identifier that may be either a GUID or a friendly name.
    /// </summary>
    /// <remarks>
    /// Microsoft Graph rejects an entire <c>$filter</c> expression with a 400
    /// if any clause compares a GUID-typed property (e.g. <c>id</c>,
    /// <c>appId</c>) to a value that isn't a valid GUID literal - even when
    /// that clause is combined with <c>or</c> against a valid string clause.
    /// So the GUID-typed clauses must only be included when
    /// <paramref name="value"/> actually parses as a GUID.
    /// </remarks>
    /// <param name="value">The caller-supplied identifier (GUID or friendly name).</param>
    /// <param name="guidProperties">GUID-typed property names (e.g. <c>id</c>, <c>appId</c>), included only when <paramref name="value"/> is a GUID.</param>
    /// <param name="stringProperties">String-typed property names (e.g. <c>displayName</c>, <c>userPrincipalName</c>), always included.</param>
    public static string BuildIdentifierFilter(
        string value,
        IReadOnlyList<string> guidProperties,
        IReadOnlyList<string> stringProperties)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        ArgumentNullException.ThrowIfNull(guidProperties);
        ArgumentNullException.ThrowIfNull(stringProperties);

        var trimmed = value.Trim();
        var escaped = EscapeODataString(trimmed);
        var stringClauses = stringProperties.Select(property => $"{property} eq '{escaped}'");

        if (!Guid.TryParse(trimmed, out _))
            return string.Join(" or ", stringClauses);

        var guidClauses = guidProperties.Select(property => $"{property} eq '{escaped}'");
        return string.Join(" or ", guidClauses.Concat(stringClauses));
    }
}
