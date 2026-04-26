using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Publisher;

[CliDestructive("Permanently deletes the publisher from the environment. Solutions using this publisher must be removed first.")]
[CliCommand(Name = "delete", Description = "Delete a solution publisher.")]
public class PublisherDeleteCliCommand : ProfiledCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(PublisherDeleteCliCommand));

    [CliArgument(Name = "name", Description = "Publisher unique name.")]
    public string Name { get; set; } = null!;

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation.", Required = false)]
    public bool Yes { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IPublisherService>();
        await service.DeleteAsync(Profile, Name, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteData(
            new { status = "deleted", uniqueName = Name },
            _ =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Deleted publisher '{Name}'.");
#pragma warning restore TXC003
            });

        return ExitSuccess;
    }
}
