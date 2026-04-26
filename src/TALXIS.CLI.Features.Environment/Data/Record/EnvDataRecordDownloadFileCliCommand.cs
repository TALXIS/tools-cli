using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
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

    [CliArgument(
        Description = "The GUID of the record containing the file.",
        ValidationPattern = @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
        ValidationMessage = "Value must be a valid GUID (e.g. 00000000-0000-0000-0000-000000000000).")]
    public Guid RecordId { get; set; }

    [CliOption(Name = "--column", Description = "Logical name of the file/image column.", Required = true)]
    public string Column { get; set; } = null!;

    [CliOption(Name = "--output", Description = "Local path where the file will be saved.", Required = true)]
    public string Output { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IDataverseFileService>();
        var fileName = await service.DownloadFileAsync(Profile, Entity, RecordId, Column, Output, CancellationToken.None)
            .ConfigureAwait(false);

        OutputWriter.WriteLine($"Downloaded '{fileName}' to {Output}");

        return ExitSuccess;
    }
}
