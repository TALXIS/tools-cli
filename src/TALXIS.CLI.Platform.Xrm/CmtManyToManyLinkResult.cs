namespace TALXIS.CLI.Platform.Xrm;

/// <summary>
/// Outcome of linking the N:N rows of a CMT data package.
/// </summary>
public sealed record CmtManyToManyLinkResult(
    int Created,
    int AlreadyLinked,
    IReadOnlyList<string> Errors);
