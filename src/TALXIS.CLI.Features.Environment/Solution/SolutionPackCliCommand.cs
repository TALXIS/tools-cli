using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Solution;

[CliIdempotent]
[CliCommand(
    Name = "pack",
    Description = "Pack an unpacked solution folder into a solution ZIP file using SolutionPackager."
)]
public class SolutionPackCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(SolutionPackCliCommand));

    [CliArgument(Name = "folder", Description = "Path to the unpacked solution folder.")]
    public string Folder { get; set; } = null!;

    [CliOption(Name = "--output", Alias = "-o", Description = "Output ZIP file path.", Required = true)]
    public string Output { get; set; } = null!;

    [CliOption(Name = "--managed", Description = "Pack as managed solution.", Required = false)]
    public bool Managed { get; set; }

    protected override Task<int> ExecuteAsync()
    {
        if (!Directory.Exists(Folder))
        {
            Logger.LogError("Folder '{Folder}' does not exist.", Folder);
            return Task.FromResult(ExitValidationError);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(Output))!);
        var packager = TxcServices.Get<ISolutionPackagerService>();
        packager.Pack(Folder, Output, Managed);

        OutputFormatter.WriteData(
            new { status = "packed", folder = Folder, output = Output, managed = Managed },
            _ =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Packed solution → {Output} ({(Managed ? "managed" : "unmanaged")})");
#pragma warning restore TXC003
            });

        return Task.FromResult(ExitSuccess);
    }
}
