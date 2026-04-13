namespace TALXIS.CLI.Environment;

public sealed record PackageDeployerRequest(
    string PackagePath,
    string? ConnectionString,
    string? EnvironmentUrl,
    bool DeviceCode,
    string? Settings,
    string? LogFile,
    bool LogConsole,
    bool Verbose);
