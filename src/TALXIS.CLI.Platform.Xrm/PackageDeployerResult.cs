namespace TALXIS.CLI.Platform.Xrm;

public sealed record PackageDeployerResult(
    bool Succeeded,
    string? ErrorMessage,
    string? LogFilePath,
    string? CmtLogFilePath,
    string? TemporaryArtifactsDirectory);
