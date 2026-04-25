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
/// Downloads a file/image column value from a Dataverse record to a local file.
/// </summary>
[CliCommand(
    Name = "download-file",
    Description = "Download a file/image column from a record."
)]
public class EnvDataRecordDownloadFileCliCommand : ProfiledCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(EnvDataRecordDownloadFileCliCommand));

    [CliOption(Name = "--entity", Description = "Entity logical name (e.g. fin_mytable).", Required = true)]
    public string Entity { get; set; } = null!;

    [CliArgument(Description = "The GUID of the record containing the file.")]
    public Guid RecordId { get; set; }

    [CliOption(Name = "--column", Description = "Logical name of the file/image column.", Required = true)]
    public string Column { get; set; } = null!;

    [CliOption(Name = "--output", Description = "Local path where the file will be saved.", Required = true)]
    public string Output { get; set; } = null!;

    public async Task<int> RunAsync()
    {
        try
        {
            var service = TxcServices.Get<IDataverseFileService>();
            var fileName = await service.DownloadFileAsync(Profile, Entity, RecordId, Column, Output, CancellationToken.None)
                .ConfigureAwait(false);

            OutputWriter.WriteLine($"Downloaded '{fileName}' to {Output}");
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or NotSupportedException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "record download-file failed");
            return 1;
        }

        return 0;
    }
}
