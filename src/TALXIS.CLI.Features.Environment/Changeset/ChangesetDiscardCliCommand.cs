using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Changeset;

/// <summary>
/// <c>txc environment changeset discard</c> — clears all staged operations.
/// </summary>
[CliDestructive("Discards all staged operations from the current changeset.")]
[CliCommand(
    Name = "discard",
    Description = "Discard all staged operations from the current changeset."
)]
#pragma warning disable TXC003
public class ChangesetDiscardCliCommand : TxcLeafCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ChangesetDiscardCliCommand));

    /// <inheritdoc />
    [CliOption(Name = "--yes", Description = "Skip interactive confirmation for this destructive operation.", Required = false)]
    public bool Yes { get; set; }

    protected override Task<int> ExecuteAsync()
    {
        var store = TxcServices.Get<IChangesetStore>();
        int count = store.Count;
        store.Clear();
        OutputWriter.WriteLine($"Discarded {count} operations.");
        return Task.FromResult(ExitSuccess);
    }
}
