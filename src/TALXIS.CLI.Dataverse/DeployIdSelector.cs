namespace TALXIS.CLI.Dataverse;

/// <summary>
/// Kinds of user-supplied <c>&lt;id&gt;</c> input accepted by <c>txc deploy show</c>.
/// </summary>
public enum DeployIdSelectorKind
{
    /// <summary><c>latest</c> keyword — pick the most recent row across both streams.</summary>
    Latest,

    /// <summary>Full 32-hex-digit GUID.</summary>
    Guid,

    /// <summary>Hex prefix of a GUID (length 4-31). Caller resolves against both streams.</summary>
    HexPrefix,

    /// <summary>Free-text name fallback — matched against <c>uniquename</c> / <c>solutionname</c>.</summary>
    Name,
}

/// <summary>
/// Result of <see cref="DeployIdSelector.Parse"/>. For <see cref="DeployIdSelectorKind.Guid"/>
/// the <see cref="Guid"/> property is populated; for <see cref="DeployIdSelectorKind.HexPrefix"/>
/// and <see cref="DeployIdSelectorKind.Name"/> the <see cref="Text"/> property holds the raw value.
/// </summary>
public sealed record DeployIdSelector(DeployIdSelectorKind Kind, Guid Guid, string Text)
{
    /// <summary>
    /// Classifies <paramref name="input"/> into one of the supported selector kinds.
    /// Never throws; falls back to <see cref="DeployIdSelectorKind.Name"/> for arbitrary strings.
    /// </summary>
    public static DeployIdSelector Parse(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var trimmed = input.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("id must not be empty.", nameof(input));
        }

        if (string.Equals(trimmed, "latest", StringComparison.OrdinalIgnoreCase))
        {
            return new DeployIdSelector(DeployIdSelectorKind.Latest, System.Guid.Empty, trimmed);
        }

        if (System.Guid.TryParse(trimmed, out var fullGuid))
        {
            return new DeployIdSelector(DeployIdSelectorKind.Guid, fullGuid, trimmed);
        }

        var stripped = trimmed.Replace("-", string.Empty, StringComparison.Ordinal);
        if (stripped.Length is >= 4 and < 32 && IsHex(stripped))
        {
            return new DeployIdSelector(DeployIdSelectorKind.HexPrefix, System.Guid.Empty, stripped.ToLowerInvariant());
        }

        return new DeployIdSelector(DeployIdSelectorKind.Name, System.Guid.Empty, trimmed);
    }

    private static bool IsHex(string s)
    {
        foreach (var c in s)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
            {
                return false;
            }
        }
        return true;
    }
}
