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

    /// <summary>Free-text name fallback — matched against <c>uniquename</c> / <c>solutionname</c>.</summary>
    Name,
}

/// <summary>
/// Result of <see cref="DeployIdSelector.Parse"/>. For <see cref="DeployIdSelectorKind.Guid"/>
/// the <see cref="Guid"/> property is populated; for <see cref="DeployIdSelectorKind.Name"/>
/// the <see cref="Text"/> property holds the raw value.
/// </summary>
public sealed record DeployIdSelector(DeployIdSelectorKind Kind, Guid Guid, string Text)
{
    /// <summary>
    /// Classifies <paramref name="input"/> into one of the supported selector kinds.
    /// Throws <see cref="ArgumentException"/> when <paramref name="input"/> is empty or whitespace.
    /// Falls back to <see cref="DeployIdSelectorKind.Name"/> for arbitrary strings that are not
    /// <c>latest</c> and not a full GUID.
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

        return new DeployIdSelector(DeployIdSelectorKind.Name, System.Guid.Empty, trimmed);
    }
}
