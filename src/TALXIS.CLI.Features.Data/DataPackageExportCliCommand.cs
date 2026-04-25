using System.ComponentModel;
using DotMake.CommandLine;
using TALXIS.CLI.Core;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Data;

[CliIdempotent]
[CliCommand(
    Name = "export",
    Description = "Export data from a Dataverse environment using a CMT schema file"
)]
public class DataPackageExportCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(DataPackageExportCliCommand));

    [CliOption(Name = "--schema", Alias = "-s", Description = "Path to the schema file (data_schema.xml) that defines which entities, fields and relationships to export. You can create this file using the Configuration Migration Tool GUI or write it by hand.", Required = true)]
    public required string Schema { get; set; }

    [CliOption(Name = "--output", Alias = "-o", Description = "Where to save the exported data package (.zip file). The zip will contain data.xml with the records and a copy of the schema.", Required = true)]
    public required string Output { get; set; }

    [CliOption(Name = "--export-files", Description = "Also download binary file and image columns (e.g. profile pictures, attachments). These are saved inside the zip in a 'files' folder. Off by default because it can be slow for large files.", Required = false)]
    [DefaultValue(false)]
    public bool ExportFiles { get; set; }

    [CliOption(Name = "--overwrite", Description = "Allow overwriting the output file if it already exists. Without this flag, the command will refuse to overwrite.", Required = false)]
    [DefaultValue(false)]
    public bool Overwrite { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(Schema))
        {
            Logger.LogError("A path to a CMT schema file must be provided.");
            return ExitError;
        }

        if (!File.Exists(Schema))
        {
            Logger.LogError("Schema file not found: {SchemaPath}", Schema);
            return ExitError;
        }

        if (string.IsNullOrWhiteSpace(Output))
        {
            Logger.LogError("An output path must be provided.");
            return ExitError;
        }

        if (File.Exists(Output) && !Overwrite)
        {
            Logger.LogError("Output file already exists: {OutputPath}. Use --overwrite to replace it.", Output);
            return ExitError;
        }

        var service = TxcServices.Get<IDataPackageService>();
        DataPackageExportResult result;
        try
        {
            result = await service.ExportAsync(Profile, Schema, Output, ExportFiles, Verbose, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Data export failed");
            return ExitError;
        }

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
            Logger.LogError("Data export failed.");
            return ExitError;
        }

        Logger.LogInformation("Data export completed successfully. Output: {OutputPath}", Path.GetFullPath(Output));
        return ExitSuccess;
    }
}
