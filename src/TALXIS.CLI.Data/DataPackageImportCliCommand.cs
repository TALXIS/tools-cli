using System.ComponentModel;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Logging;
using TALXIS.CLI.XrmTools;

namespace TALXIS.CLI.Data;

[CliCommand(
    Name = "import",
    Description = "Import a CMT data package into a Dataverse environment"
)]
public class DataPackageImportCliCommand
{
    private readonly CmtImportRunner _cmtImportRunner = new();
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(DataPackageImportCliCommand));

    [CliArgument(Description = "Path to the CMT data package (.zip file or folder containing data.xml and data_schema.xml)")]
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
            _logger.LogError("A path to a CMT data package (.zip or folder) must be provided.");
            return 1;
        }

        if (!File.Exists(Data) && !Directory.Exists(Data))
        {
            _logger.LogError("Data package not found: {DataPath}", Data);
            return 1;
        }

        string? resolvedConnectionString = ResolveConnectionString(ConnectionString);
        string? resolvedEnvironmentUrl = ResolveEnvironmentUrl(EnvironmentUrl);

        if (string.IsNullOrWhiteSpace(resolvedConnectionString) && string.IsNullOrWhiteSpace(resolvedEnvironmentUrl))
        {
            _logger.LogError("Dataverse authentication is required. Pass --connection-string, pass --environment for interactive sign-in, or set DATAVERSE_CONNECTION_STRING / TXC_DATAVERSE_CONNECTION_STRING / DATAVERSE_ENVIRONMENT_URL / TXC_DATAVERSE_ENVIRONMENT_URL.");
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
                    _logger.LogError("{ErrorMessage}", result.ErrorMessage);
                }

                _logger.LogError("Data import failed. Data package: {DataPath}", Path.GetFullPath(Data));
                return 1;
            }

            _logger.LogInformation("Data import completed successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Data import failed");
            _logger.LogError("Data package: {DataPath}", Path.GetFullPath(Data));
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
