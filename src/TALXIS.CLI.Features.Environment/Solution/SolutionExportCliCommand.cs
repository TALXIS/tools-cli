using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Solution;

[CliReadOnly]
[CliCommand(
    Name = "export",
    Description = "Export a solution from the LIVE environment as a ZIP or unpacked folder. Requires an active profile."
)]
public class SolutionExportCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(SolutionExportCliCommand));

    [CliArgument(Name = "name", Description = "Solution unique name.")]
    public string Name { get; set; } = null!;

    [CliOption(Name = "--output", Alias = "-o", Description = "Output path (directory for unpacked, file path for ZIP). Default: current directory.", Required = false)]
    public string? Output { get; set; }

    [CliOption(Name = "--managed", Description = "Export as managed solution (default: unmanaged).", Required = false)]
    public bool Managed { get; set; }

    [CliOption(Name = "--zip", Description = "Save as raw ZIP file instead of unpacking with SolutionPackager.", Required = false)]
    public bool Zip { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var outputPath = Output ?? Directory.GetCurrentDirectory();
        var unpack = !Zip;

        var options = new SolutionExportOptions(Name, Managed, outputPath, unpack);
        var service = TxcServices.Get<ISolutionExportService>();
        var resultPath = await service.ExportAsync(Profile, options, CancellationToken.None).ConfigureAwait(false);

        var mode = unpack ? "unpacked" : "ZIP";
        OutputFormatter.WriteData(
            new { status = "exported", solution = Name, managed = Managed, format = mode, path = resultPath },
            _ =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Exported solution '{Name}' ({(Managed ? "managed" : "unmanaged")}) → {resultPath} ({mode})");
#pragma warning restore TXC003
            });

        return ExitSuccess;
    }
}
