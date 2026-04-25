using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Data.Record;

/// <summary>
/// Downloads a file/image column value from a Dataverse record to a local file.
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "download-file",
    Description = "Download a file/image column from a record."
)]
#pragma warning disable TXC003
public class EnvDataRecordDownloadFileCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EnvDataRecordDownloadFileCliCommand));

    [CliOption(Name = "--entity", Description = "Entity logical name (e.g. fin_mytable).", Required = true)]
    public string Entity { get; set; } = null!;

    [CliArgument(Description = "The GUID of the record containing the file.")]
    public Guid RecordId { get; set; }

    [CliOption(Name = "--column", Description = "Logical name of the file/image column.", Required = true)]
    public string Column { get; set; } = null!;

    [CliOption(Name = "--output", Description = "Local path where the file will be saved.", Required = true)]
    public string Output { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
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
            Logger.LogError("{Error}", ex.Message);
            return ExitError;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "record download-file failed");
            return ExitError;
        }

        return ExitSuccess;
    }
}
