using System.ComponentModel;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Data;

[CliIdempotent]
[CliCommand(
    Name = "import",
    Description = "Import a CMT data package into a Dataverse environment"
)]
public class DataPackageImportCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(DataPackageImportCliCommand));

    [CliArgument(Description = "Path to the CMT data package (.zip file or folder containing data.xml and data_schema.xml)")]
    public required string Data { get; set; }

    [CliOption(Name = "--connection-count", Description = "Number of parallel connections for data import.", Required = false)]
    [DefaultValue(1)]
    public int ConnectionCount { get; set; } = 1;

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(Data))
        {
            Logger.LogError("A path to a CMT data package (.zip or folder) must be provided.");
            return ExitValidationError;
        }

        if (!File.Exists(Data) && !Directory.Exists(Data))
        {
            Logger.LogError("Data package not found: {DataPath}", Data);
            return ExitValidationError;
        }

        var service = TxcServices.Get<IDataPackageService>();
        var result = await service.ImportAsync(Profile, Data, ConnectionCount, Verbose, CancellationToken.None).ConfigureAwait(false);

        if (result.InteractiveAuthRequired)
        {
            Logger.LogError("Interactive authentication is required. Run 'txc config auth login' for profile '{Profile}' and retry.", Profile ?? "(default)");
            return ExitError;
        }

        if (!result.Succeeded)
        {
            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                Logger.LogError("{ErrorMessage}", result.ErrorMessage);
            }
            Logger.LogError("Data import failed. Data package: {DataPath}", Path.GetFullPath(Data));
            return ExitError;
        }

        Logger.LogInformation("Data import completed successfully.");
        return ExitSuccess;
    }
}
