namespace TALXIS.CLI.Platform.Xrm;

/// <summary>
/// Result of a standalone CMT data export.
/// </summary>
public sealed record CmtExportResult(
    bool Succeeded,
    string? ErrorMessage);
