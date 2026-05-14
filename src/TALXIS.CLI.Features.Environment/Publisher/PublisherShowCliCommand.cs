using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Publisher;

[CliReadOnly]
[CliCommand(Name = "get", Description = "Get details of a solution publisher.")]
public class PublisherShowCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(PublisherShowCliCommand));

    [CliArgument(Name = "name", Description = "Publisher unique name.")]
    public string Name { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IPublisherService>();
        var pub = await service.ShowAsync(Profile, Name, CancellationToken.None).ConfigureAwait(false);

        if (pub is null)
        {
            Logger.LogError("Publisher '{Name}' not found.", Name);
            return ExitError;
        }

        OutputFormatter.WriteData(pub, _ =>
        {
#pragma warning disable TXC003
            OutputWriter.WriteLine($"Publisher:  {pub.UniqueName}");
            OutputWriter.WriteLine($"Display:    {pub.FriendlyName ?? "(none)"}");
            OutputWriter.WriteLine($"Prefix:     {pub.CustomizationPrefix ?? "(none)"}");
            OutputWriter.WriteLine($"Option Val: {pub.OptionValuePrefix}");
            OutputWriter.WriteLine($"Id:         {pub.Id}");
#pragma warning restore TXC003
        });

        return ExitSuccess;
    }
}
