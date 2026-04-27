using System.ComponentModel;
using System.IO.Compression;
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
    Description = "Exports data from a LIVE Dataverse environment using a CMT schema file. Requires an active profile."
)]
public class DataPackageExportCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(DataPackageExportCliCommand));

    [CliOption(Name = "--schema", Alias = "-s", Description = "Path to the schema file (data_schema.xml) that defines which entities, fields and relationships to export. You can create this file using the Configuration Migration Tool GUI or write it by hand.", Required = true)]
    public string Schema { get; set; } = null!;

    [CliOption(Name = "--output", Alias = "-o", Description = "Directory path for the extracted data package (default), or file path to a .zip archive when --zip is used.", Required = true)]
    public string Output { get; set; } = null!;

    [CliOption(Name = "--export-files", Description = "Also download binary file and image columns (e.g. profile pictures, attachments). These are saved inside the zip in a 'files' folder. Off by default because it can be slow for large files.", Required = false)]
    [DefaultValue(false)]
    public bool ExportFiles { get; set; }

    [CliOption(Name = "--zip", Description = "Produce a .zip archive instead of extracting to a folder.", Required = false)]
    [DefaultValue(false)]
    public bool Zip { get; set; }

    [CliOption(Name = "--overwrite", Description = "Allow overwriting the output (folder or file) if it already exists. Without this flag, the command will refuse to overwrite.", Required = false)]
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

        if (Zip)
        {
            if (File.Exists(Output) && !Overwrite)
            {
                Logger.LogError("Output file already exists: {OutputPath}. Use --overwrite to replace it.", Output);
                return ExitError;
            }
        }
        else
        {
            if (Directory.Exists(Output) && Directory.EnumerateFileSystemEntries(Output).Any() && !Overwrite)
            {
                Logger.LogError("Output folder already exists and is not empty: {OutputPath}. Use --overwrite to replace it.", Output);
                return ExitError;
            }
        }

        // CMT always produces a zip. When folder output is requested, export to a temp zip first.
        var exportPath = Zip ? Output : Path.Combine(Path.GetTempPath(), $"txc-export-{Guid.NewGuid():N}.zip");

        var service = TxcServices.Get<IDataPackageService>();
        var result = await service.ExportAsync(Profile, Schema, exportPath, ExportFiles, Verbose, CancellationToken.None).ConfigureAwait(false);

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

        if (!Zip)
        {
            try
            {
                if (Directory.Exists(Output) && Overwrite)
                {
                    Directory.Delete(Output, recursive: true);
                }

                Directory.CreateDirectory(Output);
                Logger.LogInformation("Extracting to folder: {Path}", Path.GetFullPath(Output));
                ZipFile.ExtractToDirectory(exportPath, Output);
            }
            finally
            {
                if (File.Exists(exportPath))
                {
                    File.Delete(exportPath);
                }
            }
        }

        Logger.LogInformation("Data export completed successfully. Output: {OutputPath}", Path.GetFullPath(Output));
        return ExitSuccess;
    }
}
