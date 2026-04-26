using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Data.Record;

/// <summary>
/// Uploads a local file to a file/image column on a Dataverse record.
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "upload-file",
    Description = "Upload a file to a file/image column on a record."
)]
#pragma warning disable TXC003
public class EnvDataRecordUploadFileCliCommand : StagedCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EnvDataRecordUploadFileCliCommand));

    [CliOption(Name = "--entity", Description = "Entity logical name (e.g. fin_mytable).", Required = true)]
    public string Entity { get; set; } = null!;

    [CliArgument(
        Description = "The GUID of the record to upload the file to.",
        ValidationPattern = CliValidation.GuidPattern,
        ValidationMessage = CliValidation.GuidValidationMessage)]
    public Guid RecordId { get; set; }

    [CliOption(Name = "--column", Description = "Logical name of the file/image column.", Required = true)]
    public string Column { get; set; } = null!;

    [CliOption(Name = "--file", Description = "Path to the local file to upload.", Required = true)]
    public string File { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
    {
        ValidateExecutionMode();

        if (Stage)
        {
            var store = TxcServices.Get<IChangesetStore>();
            store.Add(new StagedOperation
            {
                Category = "data",
                OperationType = "UPLOAD",
                TargetType = "file",
                TargetDescription = $"{Entity}/{RecordId}/{Column}",
                Details = $"file: {File}",
                Parameters = new Dictionary<string, object?>
                {
                    ["entity"] = Entity,
                    ["recordId"] = RecordId.ToString(),
                    ["column"] = Column,
                    ["file"] = File
                }
            });
            OutputWriter.WriteLine($"Staged: UPLOAD file to {Entity}/{RecordId}/{Column}");
            return ExitSuccess;
        }

        if (!System.IO.File.Exists(File))
        {
            Logger.LogError("File not found: {Path}", File);
            return ExitError;
        }

        var service = TxcServices.Get<IDataverseFileService>();
        await service.UploadFileAsync(Profile, Entity, RecordId, Column, File, CancellationToken.None)
            .ConfigureAwait(false);

        OutputWriter.WriteLine($"Uploaded '{Path.GetFileName(File)}' to {Entity}/{RecordId}/{Column}");

        return ExitSuccess;
    }
}
