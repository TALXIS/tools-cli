namespace TALXIS.CLI.Deploy;

/// <summary>
/// Kinds of user-supplied identifier accepted by <c>txc deploy show</c>.
/// </summary>
public enum DeployIdSelectorKind
{
    /// <summary><c>latest</c> keyword — pick the most recent row across both streams.</summary>
    Latest,

    /// <summary>Full 32-hex-digit GUID (packagehistory id, msdyn_solutionhistoryid, or asyncOperationId).</summary>
    Guid,

    /// <summary>NuGet package name — matched against <c>packagehistory.uniquename</c> only.</summary>
    PackageName,

    /// <summary>Solution unique name — matched against <c>msdyn_solutionhistory.msdyn_uniquename</c> only.</summary>
    SolutionName,
}

/// <summary>
/// Result of <see cref="DeployIdSelector.Parse"/>. For <see cref="DeployIdSelectorKind.Guid"/>
/// the <see cref="Guid"/> property is populated; for name-based kinds the <see cref="Text"/>
/// property holds the raw value.
/// </summary>
public sealed record DeployIdSelector(DeployIdSelectorKind Kind, Guid Guid, string Text)
{
    /// <summary>
    /// Parses <paramref name="input"/> as a <c>latest</c> keyword or a full GUID.
    /// Throws <see cref="ArgumentException"/> when <paramref name="input"/> is empty or whitespace,
    /// and <see cref="FormatException"/> when it is neither <c>latest</c> nor a valid GUID.
    /// Use <see cref="DeployIdSelectorKind.PackageName"/> or <see cref="DeployIdSelectorKind.SolutionName"/>
    /// directly for name-based lookups.
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

        throw new FormatException($"'{trimmed}' is not a valid GUID. Use --latest, --package-name, or --solution-name for non-GUID lookups.");
    }
}
