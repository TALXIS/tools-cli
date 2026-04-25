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

    [CliOption(Name = "--connection-count", Description = "How many parallel connections to open against the environment. More connections = faster import for large datasets. Each connection authenticates separately.", Required = false)]
    [DefaultValue(1)]
    public int ConnectionCount { get; set; } = 1;

    [CliOption(Name = "--batch-mode", Description = "Send records in batches instead of one-by-one. Much faster for large imports. Batches use ExecuteMultiple or UpsertMultiple depending on org version.", Required = false)]
    [DefaultValue(false)]
    public bool BatchMode { get; set; }

    [CliOption(Name = "--batch-size", Description = "How many records to send per batch request. Only used when --batch-mode is on. Lower values are safer, higher values are faster.", Required = false)]
    [DefaultValue(600)]
    public int BatchSize { get; set; } = 600;

    [CliOption(Name = "--override-safety-checks", Description = "DANGEROUS: Skip all duplicate checking. Every record will be created as new, even if it already exists. Use only when importing into a clean empty environment.", Required = false)]
    [DefaultValue(false)]
    public bool OverrideSafetyChecks { get; set; }

    [CliOption(Name = "--prefetch-limit", Description = "How many existing records to load into memory per entity for faster duplicate detection. If an entity has more records than this limit, each record is checked individually against the server (slower). Increase for large entities.", Required = false)]
    [DefaultValue(4000)]
    public int PrefetchLimit { get; set; } = 4000;

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
        var result = await service.ImportAsync(Profile, Data, ConnectionCount, BatchMode, BatchSize, OverrideSafetyChecks, PrefetchLimit, Verbose, CancellationToken.None).ConfigureAwait(false);

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
