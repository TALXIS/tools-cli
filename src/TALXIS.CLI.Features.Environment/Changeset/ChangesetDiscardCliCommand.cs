using DotMake.CommandLine;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;

namespace TALXIS.CLI.Features.Environment.Changeset;

/// <summary>
/// <c>txc environment changeset discard</c> — clears all staged operations.
/// </summary>
[CliCommand(
    Name = "discard",
    Description = "Discard all staged operations from the current changeset."
)]
public class ChangesetDiscardCliCommand
{
    public void Run()
    {
        var store = TxcServices.Get<IChangesetStore>();
        int count = store.Count;
        store.Clear();
        OutputWriter.WriteLine($"Discarded {count} operations.");
    }
}
