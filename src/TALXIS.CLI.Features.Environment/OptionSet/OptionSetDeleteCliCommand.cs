using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.OptionSet;

[CliDestructive("Permanently deletes the global option set from the environment.")]
[CliCommand(
    Name = "delete",
    Description = "Delete a global option set (choice)."
)]
#pragma warning disable TXC003
public class OptionSetDeleteCliCommand : StagedCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(OptionSetDeleteCliCommand));

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation.", Required = false)]
    public bool Yes { get; set; }

    [CliOption(Name = "--name", Description = "Schema name of the global option set to delete.", Required = true)]
    public string Name { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
    {
        ValidateExecutionMode();

        if (Stage)
        {
            var store = TxcServices.Get<IChangesetStore>();
            store.Add(new StagedOperation
            {
                Category = "schema",
                OperationType = "DELETE",
                TargetType = "optionset",
                TargetDescription = Name,
                Details = $"global optionset: \"{Name}\"",
                Parameters = new Dictionary<string, object?> { ["name"] = Name }
            });
            OutputWriter.WriteLine($"Staged: DELETE global optionset '{Name}'");
            return ExitSuccess;
        }

        var service = TxcServices.Get<IDataverseOptionSetService>();
        await service.DeleteGlobalOptionSetAsync(Profile, Name, CancellationToken.None).ConfigureAwait(false);

        OutputWriter.WriteLine($"Global option set '{Name}' deleted.");
        return ExitSuccess;
    }
}
