using System.ComponentModel;
using DotMake.CommandLine;
using TALXIS.CLI.XrmTools;

namespace TALXIS.CLI.Data;

[CliCommand(
    Name = "import",
    Description = "Import a CMT data package into a Dataverse environment"
)]
public class DataPackageImportCliCommand
{
    private readonly CmtImportRunner _cmtImportRunner = new();

    [CliArgument(Description = "Path to the CMT data package (.zip)")]
    public required string Data { get; set; }

    [CliOption(Name = "--connection-string", Description = "Dataverse connection string. If omitted, txc checks DATAVERSE_CONNECTION_STRING and TXC_DATAVERSE_CONNECTION_STRING.", Required = false)]
    public string? ConnectionString { get; set; }

    [CliOption(Name = "--environment", Description = "Dataverse environment URL for interactive sign-in when no connection string is provided.", Required = false)]
    public string? EnvironmentUrl { get; set; }

    [CliOption(Name = "--device-code", Description = "Use Microsoft Entra device code flow instead of opening a browser for interactive sign-in.", Required = false)]
    public bool DeviceCode { get; set; }

    [CliOption(Name = "--connection-count", Description = "Number of parallel connections for data import.", Required = false)]
    [DefaultValue(1)]
    public int ConnectionCount { get; set; } = 1;

    [CliOption(Name = "--verbose", Description = "Enable verbose CMT logging.", Required = false)]
    public bool Verbose { get; set; }

    public async Task<int> RunAsync()
    {
        if (string.IsNullOrWhiteSpace(Data))
        {
            Console.Error.WriteLine("A path to a CMT data package (.zip) must be provided.");
            return 1;
        }

        if (!File.Exists(Data))
        {
            Console.Error.WriteLine($"Data package not found: '{Data}'");
            return 1;
        }

        string? resolvedConnectionString = ResolveConnectionString(ConnectionString);
        string? resolvedEnvironmentUrl = ResolveEnvironmentUrl(EnvironmentUrl);

        if (string.IsNullOrWhiteSpace(resolvedConnectionString) && string.IsNullOrWhiteSpace(resolvedEnvironmentUrl))
        {
            Console.Error.WriteLine(
                "Dataverse authentication is required. Pass --connection-string, pass --environment for interactive sign-in, or set DATAVERSE_CONNECTION_STRING / TXC_DATAVERSE_CONNECTION_STRING / DATAVERSE_ENVIRONMENT_URL / TXC_DATAVERSE_ENVIRONMENT_URL.");
            return 1;
        }

        try
        {
            CmtImportResult result = await _cmtImportRunner.RunAsync(new CmtImportRequest(
                Path.GetFullPath(Data),
                resolvedConnectionString,
                resolvedEnvironmentUrl,
                DeviceCode,
                ConnectionCount,
                Verbose));

            if (!result.Succeeded)
            {
                if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    Console.Error.WriteLine(result.ErrorMessage);
                }

                Console.Error.WriteLine($"Data import failed. Data package: '{Path.GetFullPath(Data)}'.");
                return 1;
            }

            Console.WriteLine("Data import completed successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine($"Data package: '{Path.GetFullPath(Data)}'.");
            return 1;
        }
    }

    private static string? ResolveConnectionString(string? optionValue)
    {
        if (!string.IsNullOrWhiteSpace(optionValue))
        {
            return optionValue;
        }

        return Environment.GetEnvironmentVariable("DATAVERSE_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("TXC_DATAVERSE_CONNECTION_STRING");
    }

    private static string? ResolveEnvironmentUrl(string? optionValue)
    {
        if (!string.IsNullOrWhiteSpace(optionValue))
        {
            return optionValue;
        }

        return Environment.GetEnvironmentVariable("DATAVERSE_ENVIRONMENT_URL")
            ?? Environment.GetEnvironmentVariable("TXC_DATAVERSE_ENVIRONMENT_URL");
    }
}
