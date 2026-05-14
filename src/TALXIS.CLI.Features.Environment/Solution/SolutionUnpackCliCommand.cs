using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;
using TALXIS.Platform.Metadata.Packaging;

namespace TALXIS.CLI.Features.Environment.Solution;

[CliIdempotent]
[CliCommand(
    Name = "unpack",
    Description = "Unpack a solution ZIP file into a folder using SolutionPackager."
)]
public class SolutionUnpackCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(SolutionUnpackCliCommand));

    [CliArgument(Name = "zip-file", Description = "Path to the solution ZIP file.")]
    public string ZipFile { get; set; } = null!;

    [CliOption(Name = "--output", Alias = "-o", Description = "Output folder path for unpacked solution.", Required = true)]
    public string Output { get; set; } = null!;

    [CliOption(Name = "--managed", Description = "Treat the ZIP as a managed solution.", Required = false)]
    public bool Managed { get; set; }

    protected override Task<int> ExecuteAsync()
    {
        if (!File.Exists(ZipFile))
        {
            Logger.LogError("ZIP file '{ZipFile}' does not exist.", ZipFile);
            return Task.FromResult(ExitValidationError);
        }

        Directory.CreateDirectory(Output);
        var packager = TxcServices.Get<ISolutionPackagerService>();
        packager.Unpack(ZipFile, Output, Managed);

        OutputFormatter.WriteData(
            new { status = "unpacked", zipFile = ZipFile, output = Output, managed = Managed },
            _ =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Unpacked solution → {Output} ({(Managed ? "managed" : "unmanaged")})");
#pragma warning restore TXC003
            });

        return Task.FromResult(ExitSuccess);
    }
}
