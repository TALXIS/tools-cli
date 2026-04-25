using System.ComponentModel;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Features.Config.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Data;

[CliCommand(
    Name = "export",
    Description = "Export data from a Dataverse environment using a CMT schema file"
)]
public class DataPackageExportCliCommand : ProfiledCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(DataPackageExportCliCommand));

    [CliOption(Name = "--schema", Alias = "-s", Description = "Path to the CMT schema file (data_schema.xml) defining entities and fields to export.", Required = true)]
    public required string Schema { get; set; }

    [CliOption(Name = "--output", Alias = "-o", Description = "Path for the output data package (.zip file).", Required = true)]
    public required string Output { get; set; }

    [CliOption(Name = "--export-files", Description = "Include binary file and image columns in the export.", Required = false)]
    [DefaultValue(false)]
    public bool ExportFiles { get; set; }

    [CliOption(Name = "--overwrite", Description = "Overwrite the output file if it already exists.", Required = false)]
    [DefaultValue(false)]
    public bool Overwrite { get; set; }

    public async Task<int> RunAsync()
    {
        if (string.IsNullOrWhiteSpace(Schema))
        {
            _logger.LogError("A path to a CMT schema file must be provided.");
            return 1;
        }

        if (!File.Exists(Schema))
        {
            _logger.LogError("Schema file not found: {SchemaPath}", Schema);
            return 1;
        }

        if (string.IsNullOrWhiteSpace(Output))
        {
            _logger.LogError("An output path must be provided.");
            return 1;
        }

        if (File.Exists(Output) && !Overwrite)
        {
            _logger.LogError("Output file already exists: {OutputPath}. Use --overwrite to replace it.", Output);
            return 1;
        }

        var service = TxcServices.Get<IDataPackageService>();
        DataPackageExportResult result;
        try
        {
            result = await service.ExportAsync(Profile, Schema, Output, ExportFiles, Verbose, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Data export failed");
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
            _logger.LogError("Data export failed.");
            return 1;
        }

        _logger.LogInformation("Data export completed successfully. Output: {OutputPath}", Path.GetFullPath(Output));
        return 0;
    }
}
