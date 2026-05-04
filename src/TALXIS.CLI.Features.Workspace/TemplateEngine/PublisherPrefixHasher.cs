namespace TALXIS.CLI.Features.Workspace.TemplateEngine;

/// <summary>
/// Computes a deterministic option-value prefix (10 000–99 999) from a publisher
/// customization prefix string. Used by post-action processors to replace placeholder
/// values in scaffolded Solution.xml files.
/// </summary>
public static class PublisherPrefixHasher
{
    private const string SpecialUuid = "D21AAB71-79E7-11DD-8874-00188B01E34F";

    /// <summary>
    /// Returns a deterministic integer in the range [10 000, 99 999] derived from
    /// the given publisher prefix (case-insensitive).
    /// </summary>
    public static int ComputeOptionValuePrefix(string input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);

        if (input.Equals(SpecialUuid, StringComparison.InvariantCultureIgnoreCase)) return 10_000;

        int hash = 0;
        foreach (char c in input.ToUpperInvariant())
        {
            hash = (hash << 5) - hash + c;
            hash &= hash;
        }

        return Math.Abs(hash) % 90_000 + 10_000;
    }
}
