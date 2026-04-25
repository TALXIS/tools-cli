using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Config.Abstractions;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>
/// Deletes an existing global option set (choice) from Dataverse.
/// Usage: <c>txc environment entity optionset delete-global --name &lt;schema-name&gt; [-p profile] --apply</c>
/// </summary>
[CliCommand(
    Name = "delete-global",
    Description = "Delete a global option set (choice)."
)]
public class EntityOptionSetDeleteGlobalCliCommand : StagedCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(EntityOptionSetDeleteGlobalCliCommand));

    [CliOption(Name = "--name", Description = "The schema name of the global option set to delete.", Required = true)]
    public string Name { get; set; } = null!;

    public async Task<int> RunAsync()
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
                Parameters = new Dictionary<string, object?>
                {
                    ["name"] = Name
                }
            });
            OutputWriter.WriteLine($"Staged: DELETE global optionset '{Name}'");
            return 0;
        }

        try
        {
            var service = TxcServices.Get<IDataverseOptionSetService>();
            await service.DeleteGlobalOptionSetAsync(
                Profile, Name, CancellationToken.None
            ).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or ArgumentException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "environment entity optionset delete-global failed");
            return 1;
        }

        OutputWriter.WriteLine($"Global option set '{Name}' deleted successfully.");
        return 0;
    }
}
