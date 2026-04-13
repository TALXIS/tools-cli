namespace TALXIS.CLI.Environment;

public sealed record PackageDeployerResult(
    bool Succeeded,
    string? ErrorMessage);
