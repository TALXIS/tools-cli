using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Config.Abstractions;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Data.Record;

/// <summary>
/// Uploads a local file to a file/image column on a Dataverse record.
/// </summary>
[CliCommand(
    Name = "upload-file",
    Description = "Upload a file to a file/image column on a record."
)]
public class EnvDataRecordUploadFileCliCommand : ProfiledCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(EnvDataRecordUploadFileCliCommand));

    [CliOption(Name = "--entity", Description = "Entity logical name (e.g. fin_mytable).", Required = true)]
    public string Entity { get; set; } = null!;

    [CliArgument(Description = "The GUID of the record to upload the file to.")]
    public Guid RecordId { get; set; }

    [CliOption(Name = "--column", Description = "Logical name of the file/image column.", Required = true)]
    public string Column { get; set; } = null!;

    [CliOption(Name = "--file", Description = "Path to the local file to upload.", Required = true)]
    public string File { get; set; } = null!;

    public async Task<int> RunAsync()
    {
        try
        {
            if (!System.IO.File.Exists(File))
            {
                _logger.LogError("File not found: {Path}", File);
                return 1;
            }

            var service = TxcServices.Get<IDataverseFileService>();
            await service.UploadFileAsync(Profile, Entity, RecordId, Column, File, CancellationToken.None)
                .ConfigureAwait(false);

            OutputWriter.WriteLine($"Uploaded '{Path.GetFileName(File)}' to {Entity}/{RecordId}/{Column}");
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or NotSupportedException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "record upload-file failed");
            return 1;
        }

        return 0;
    }
}
