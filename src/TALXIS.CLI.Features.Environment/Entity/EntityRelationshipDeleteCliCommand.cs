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
/// Deletes a relationship from Dataverse by its schema name.
/// Usage: <c>txc environment entity relationship delete --name &lt;relationship-schema-name&gt;</c>
/// </summary>
[CliCommand(
    Name = "delete",
    Description = "Delete a relationship by its schema name."
)]
public class EntityRelationshipDeleteCliCommand : ProfiledCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(EntityRelationshipDeleteCliCommand));

    [CliOption(Name = "--name", Description = "The schema name of the relationship to delete.", Required = true)]
    public string Name { get; set; } = null!;

    public async Task<int> RunAsync()
    {
        try
        {
            var service = TxcServices.Get<IDataverseEntityMetadataService>();
            await service.DeleteRelationshipAsync(
                Profile, Name, CancellationToken.None
            ).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "environment entity relationship delete failed");
            return 1;
        }

        OutputWriter.WriteLine($"Relationship '{Name}' deleted successfully.");
        return 0;
    }
}
