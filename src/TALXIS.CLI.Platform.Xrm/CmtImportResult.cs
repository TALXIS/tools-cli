namespace TALXIS.CLI.Platform.Xrm;

/// <summary>
/// Result of a standalone CMT data import.
/// </summary>
public sealed record CmtImportResult(
    bool Succeeded,
    string? ErrorMessage);
