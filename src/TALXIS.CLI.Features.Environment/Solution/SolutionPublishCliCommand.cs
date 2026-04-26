using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Solution;

[CliIdempotent]
[CliCommand(
    Name = "publish",
    Description = "Publish all customizations or a selective set of entities."
)]
public class SolutionPublishCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(SolutionPublishCliCommand));

    [CliOption(Name = "--entities", Description = "Comma-separated entity logical names for selective publish. Omit to publish all.", Required = false)]
    public string? Entities { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        IReadOnlyList<string>? entityNames = null;
        if (!string.IsNullOrWhiteSpace(Entities))
        {
            entityNames = Entities.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        var service = TxcServices.Get<ISolutionPublishService>();
        await service.PublishAsync(Profile, entityNames, CancellationToken.None).ConfigureAwait(false);

        var msg = entityNames is { Count: > 0 }
            ? $"Published customizations for {entityNames.Count} entity(ies): {string.Join(", ", entityNames)}."
            : "Published all customizations.";

        OutputFormatter.WriteData(new { status = "published", entities = entityNames }, _ =>
        {
#pragma warning disable TXC003
            OutputWriter.WriteLine(msg);
#pragma warning restore TXC003
        });

        return ExitSuccess;
    }
}
