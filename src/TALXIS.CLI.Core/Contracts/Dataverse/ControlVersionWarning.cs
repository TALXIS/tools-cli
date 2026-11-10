namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// A PCF control from the imported solution that Dataverse did not (or will not) update.
/// </summary>
public sealed record ControlVersionWarning(
    string ControlName,
    string PackagedVersion,
    string? InstalledVersion,
    string Message);
