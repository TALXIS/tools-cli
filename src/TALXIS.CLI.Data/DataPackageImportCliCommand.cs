using System.ComponentModel;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.Commands.Abstractions;
using TALXIS.CLI.Config.Providers.Dataverse.Runtime;
using TALXIS.CLI.Logging;
using TALXIS.CLI.XrmTools;

namespace TALXIS.CLI.Data;

[CliCommand(
    Name = "import",
    Description = "Import a CMT data package into a Dataverse environment"
)]
public class DataPackageImportCliCommand : ProfiledCliCommand
{
    private readonly CmtImportRunner _cmtImportRunner = new();
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(DataPackageImportCliCommand));

    [CliArgument(Description = "Path to the CMT data package (.zip file or folder containing data.xml and data_schema.xml)")]
    public required string Data { get; set; }

    [CliOption(Name = "--connection-count", Description = "Number of parallel connections for data import.", Required = false)]
    [DefaultValue(1)]
    public int ConnectionCount { get; set; } = 1;

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

        string resolvedConnectionString;
        try
        {
            resolvedConnectionString = await DataverseCommandBridge.BuildConnectionStringAsync(Profile, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or NotSupportedException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }

        try
        {
            CmtImportResult result = await _cmtImportRunner.RunAsync(new CmtImportRequest(
                Path.GetFullPath(Data),
                resolvedConnectionString,
                EnvironmentUrl: null,
                DeviceCode: false,
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
}
