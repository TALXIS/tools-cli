using System.ComponentModel;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Features.Config.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Data;

[CliCommand(
    Name = "import",
    Description = "Import a CMT data package into a Dataverse environment"
)]
public class DataPackageImportCliCommand : ProfiledCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(DataPackageImportCliCommand));

    [CliArgument(Description = "Path to the CMT data package (.zip file or folder containing data.xml and data_schema.xml)")]
    public required string Data { get; set; }

    [CliOption(Name = "--connection-count", Description = "Number of parallel connections for data import.", Required = false)]
    [DefaultValue(1)]
    public int ConnectionCount { get; set; } = 1;

    [CliOption(Name = "--batch-mode", Description = "Enable batch mode for import (ExecuteMultiple/UpsertMultiple).", Required = false)]
    [DefaultValue(false)]
    public bool BatchMode { get; set; }

    [CliOption(Name = "--batch-size", Description = "Number of records per batch request (requires --batch-mode).", Required = false)]
    [DefaultValue(600)]
    public int BatchSize { get; set; } = 600;

    [CliOption(Name = "--override-safety-checks", Description = "Skip duplicate detection — always create records, never update existing ones.", Required = false)]
    [DefaultValue(false)]
    public bool OverrideSafetyChecks { get; set; }

    [CliOption(Name = "--prefetch-limit", Description = "Maximum number of records to preload into cache per entity for duplicate detection.", Required = false)]
    [DefaultValue(4000)]
    public int PrefetchLimit { get; set; } = 4000;

    [CliOption(Name = "--delete-before-import", Description = "Delete existing records before importing new data.", Required = false)]
    [DefaultValue(false)]
    public bool DeleteBeforeImport { get; set; }

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

        var service = TxcServices.Get<IDataPackageService>();
        DataPackageImportResult result;
        try
        {
            result = await service.ImportAsync(Profile, Data, ConnectionCount, BatchMode, BatchSize, OverrideSafetyChecks, PrefetchLimit, DeleteBeforeImport, Verbose, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Data import failed");
            _logger.LogError("Data package: {DataPath}", Path.GetFullPath(Data));
            return 1;
        }

        if (result.InteractiveAuthRequired)
        {
            _logger.LogError("Interactive authentication is required. Run 'txc config auth login' for profile '{Profile}' and retry.", Profile ?? "(default)");
            return 1;
        }

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
}
