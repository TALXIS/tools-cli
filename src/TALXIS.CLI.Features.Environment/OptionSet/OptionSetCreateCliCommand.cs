using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.OptionSet;

/// <summary>
/// Creates a new global option set (choice) in Dataverse.
/// Usage: <c>txc environment optionset create --name &lt;schema-name&gt; --display-name &lt;label&gt; --options &lt;csv&gt;</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "create",
    Description = "Create a new global option set (choice)."
)]
#pragma warning disable TXC003
public class OptionSetCreateCliCommand : StagedCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(OptionSetCreateCliCommand));

    [CliOption(Name = "--name", Description = "Schema name of the global option set.", Required = true)]
    public string Name { get; set; } = null!;

    [CliOption(Name = "--display-name", Description = "Display name (label) for the option set.", Required = true)]
    public string DisplayName { get; set; } = null!;

    [CliOption(Name = "--options", Description = "Comma-separated options: \"Label1:100000000,Label2:100000001\" or \"Label1,Label2\" (auto-value).", Required = true)]
    public string Options { get; set; } = null!;

    [CliOption(Name = "--description", Description = "Description for the option set.", Required = false)]
    public string? Description { get; set; }

    [CliOption(Name = "--solution", Description = "Solution unique name to register the option set in.", Required = false)]
    public string? Solution { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        ValidateExecutionMode();

        if (Stage)
        {
            var store = TxcServices.Get<IChangesetStore>();
            store.Add(new StagedOperation
            {
                Category = "schema",
                OperationType = "CREATE",
                TargetType = "optionset",
                TargetDescription = Name,
                Details = $"display: \"{DisplayName}\", options: \"{Options}\"",
                Parameters = new Dictionary<string, object?>
                {
                    ["name"] = Name,
                    ["displayName"] = DisplayName,
                    ["options"] = Options,
                    ["description"] = Description,
                    ["solution"] = Solution
                }
            });
            OutputWriter.WriteLine($"Staged: CREATE global optionset '{Name}'");
            return ExitSuccess;
        }

        OptionMetadataInput[] parsed = OptionMetadataInput.ParseCsv(Options);

        var service = TxcServices.Get<IDataverseOptionSetService>();
        await service.CreateGlobalOptionSetAsync(
            Profile, Name, DisplayName, Description, parsed, Solution, CancellationToken.None
        ).ConfigureAwait(false);

        OutputWriter.WriteLine($"Global option set '{Name}' created.");
        return ExitSuccess;
    }
}
