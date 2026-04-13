namespace TALXIS.CLI.Environment;

public sealed record EnvironmentInstallOptions(
    string PackageName,
    string PackageVersion,
    string? DeployablePackageName,
    string? OutputDirectory,
    bool DownloadOnly,
    string? ConnectionString,
    string? EnvironmentUrl,
    bool DeviceCode,
    string? Settings,
    string? LogFile,
    bool LogConsole,
    bool Verbose);
