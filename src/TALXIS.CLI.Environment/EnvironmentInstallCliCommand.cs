using System.ComponentModel;
using DotMake.CommandLine;

namespace TALXIS.CLI.Environment;

[CliCommand(
    Name = "install",
    Description = "Downloads a Dataverse deployable package from nuget.org and deploys it with Package Deployer"
)]
public class EnvironmentInstallCliCommand
{
    private readonly NuGetPackageInstallerService _packageInstaller = new();
    private readonly PackageDeployerRunner _packageDeployerRunner = new();

    [CliArgument(Description = "NuGet package name to download and install")]
    public required string PackageName { get; set; }

    [CliOption(Name = "--version", Description = "NuGet package version to install. Defaults to 'latest'.", Required = false)]
    [DefaultValue("latest")]
    public string PackageVersion { get; set; } = "latest";

    [CliOption(Name = "--deployable-package", Description = "Optional deployable package file name inside the NuGet package. Use when the package contains multiple .pdpkg files.", Required = false)]
    public string? DeployablePackageName { get; set; }

    [CliOption(Name = "--output", Aliases = ["-o"], Description = "Directory where the downloaded and extracted files should be written. Defaults to a temporary directory.", Required = false)]
    public string? OutputDirectory { get; set; }

    [CliOption(Name = "--download-only", Description = "Download and extract the deployable package without invoking Package Deployer.", Required = false)]
    public bool DownloadOnly { get; set; }

    [CliOption(Name = "--connection-string", Description = "Dataverse connection string used by the managed Package Deployer host. If omitted, txc checks DATAVERSE_CONNECTION_STRING and TXC_DATAVERSE_CONNECTION_STRING.", Required = false)]
    public string? ConnectionString { get; set; }

    [CliOption(Name = "--environment", Description = "Dataverse environment URL used for PAC-style interactive sign-in when no connection string is provided.", Required = false)]
    public string? EnvironmentUrl { get; set; }

    [CliOption(Name = "--device-code", Description = "Use Microsoft Entra device code flow instead of opening a browser for interactive sign-in.", Required = false)]
    public bool DeviceCode { get; set; }

    [CliOption(Name = "--settings", Description = "Runtime settings string passed to Package Deployer.", Required = false)]
    public string? Settings { get; set; }

    [CliOption(Name = "--log-file", Description = "Optional Package Deployer log file path.", Required = false)]
    public string? LogFile { get; set; }

    [CliOption(Name = "--log-console", Description = "Enable Package Deployer console logging.", Required = false)]
    public bool LogConsole { get; set; }

    [CliOption(Name = "--verbose", Description = "Enable verbose Package Deployer logging.", Required = false)]
    public bool Verbose { get; set; }

    public async Task<int> RunAsync()
    {
        if (string.IsNullOrWhiteSpace(PackageName))
        {
            Console.Error.WriteLine("A NuGet package name must be provided.");
            return 1;
        }

        EnvironmentInstallOptions options = new(
            PackageName,
            PackageVersion,
            DeployablePackageName,
            OutputDirectory,
            DownloadOnly,
            ResolveConnectionString(ConnectionString),
            ResolveEnvironmentUrl(EnvironmentUrl),
            DeviceCode,
            Settings,
            LogFile,
            LogConsole,
            Verbose);

        NuGetPackageInstallResult installResult = await _packageInstaller.InstallAsync(options);

        Console.WriteLine($"Resolved '{installResult.PackageName}' version '{installResult.ResolvedVersion}'.");
        Console.WriteLine($"Deployable package extracted to '{installResult.DeployablePackagePath}'.");

        if (DownloadOnly)
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(options.ConnectionString) && string.IsNullOrWhiteSpace(options.EnvironmentUrl))
        {
            Console.Error.WriteLine(
                "Dataverse authentication is required to run Package Deployer. Pass --connection-string, pass --environment for interactive sign-in, or set DATAVERSE_CONNECTION_STRING / TXC_DATAVERSE_CONNECTION_STRING / DATAVERSE_ENVIRONMENT_URL / TXC_DATAVERSE_ENVIRONMENT_URL.");
            Console.Error.WriteLine($"The extracted deployable package was kept at '{installResult.DeployablePackagePath}'.");
            return 1;
        }

        try
        {
            PackageDeployerResult deployResult = await _packageDeployerRunner.RunAsync(new PackageDeployerRequest(
                installResult.DeployablePackagePath,
                options.ConnectionString,
                options.EnvironmentUrl,
                options.DeviceCode,
                Settings,
                LogFile,
                LogConsole,
                Verbose));

            if (!deployResult.Succeeded)
            {
                if (!string.IsNullOrWhiteSpace(deployResult.ErrorMessage))
                {
                    Console.Error.WriteLine(deployResult.ErrorMessage);
                }

                Console.Error.WriteLine(
                    $"Package deploy failed. The extracted deployable package was kept at '{installResult.DeployablePackagePath}'.");
                return 1;
            }

            if (installResult.UsesTemporaryWorkingDirectory)
            {
                Directory.Delete(installResult.WorkingDirectory, recursive: true);
            }

            Console.WriteLine("Package deploy completed successfully.");
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine($"The extracted deployable package was kept at '{installResult.DeployablePackagePath}'.");
            return 1;
        }
    }

    private static string? ResolveConnectionString(string? optionValue)
    {
        if (!string.IsNullOrWhiteSpace(optionValue))
        {
            return optionValue;
        }

        return System.Environment.GetEnvironmentVariable("DATAVERSE_CONNECTION_STRING")
            ?? System.Environment.GetEnvironmentVariable("TXC_DATAVERSE_CONNECTION_STRING");
    }

    private static string? ResolveEnvironmentUrl(string? optionValue)
    {
        if (!string.IsNullOrWhiteSpace(optionValue))
        {
            return optionValue;
        }

        return System.Environment.GetEnvironmentVariable("DATAVERSE_ENVIRONMENT_URL")
            ?? System.Environment.GetEnvironmentVariable("TXC_DATAVERSE_ENVIRONMENT_URL");
    }
}
