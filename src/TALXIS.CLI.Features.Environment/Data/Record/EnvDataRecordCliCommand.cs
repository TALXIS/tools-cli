using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Data.Record;

/// <summary>
/// Parent command for single-record CRUD operations against a live
/// Dataverse environment.
/// </summary>
[CliCommand(
    Name = "record",
    Description = "Single-record CRUD operations against the environment.",
    Children = new[] { typeof(EnvDataRecordGetCliCommand), typeof(EnvDataRecordCreateCliCommand), typeof(EnvDataRecordUpdateCliCommand), typeof(EnvDataRecordDeleteCliCommand), typeof(EnvDataRecordDownloadFileCliCommand), typeof(EnvDataRecordUploadFileCliCommand), typeof(EnvDataRecordAssociateCliCommand), typeof(EnvDataRecordDisassociateCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class EnvDataRecordCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
