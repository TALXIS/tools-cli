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
/// Creates a many-to-many (N:N) relationship between two entities.
/// Usage: <c>txc environment entity relationship create --entity1 &lt;name&gt; --entity2 &lt;name&gt; --name &lt;schema-name&gt; [--display-name &lt;label&gt;]</c>
/// </summary>
[CliCommand(
    Name = "create",
    Description = "Create a many-to-many (N:N) relationship between two entities."
)]
public class EntityRelationshipCreateCliCommand : ProfiledCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(EntityRelationshipCreateCliCommand));

    [CliOption(Name = "--entity1", Description = "The logical name of the first entity.", Required = true)]
    public string Entity1 { get; set; } = null!;

    [CliOption(Name = "--entity2", Description = "The logical name of the second entity.", Required = true)]
    public string Entity2 { get; set; } = null!;

    [CliOption(Name = "--name", Description = "The schema name for the relationship and intersect entity.", Required = true)]
    public string Name { get; set; } = null!;

    [CliOption(Name = "--display-name", Description = "The display name (label) for the relationship menu items.", Required = false)]
    public string? DisplayName { get; set; }

    public async Task<int> RunAsync()
    {
        try
        {
            var service = TxcServices.Get<IDataverseEntityMetadataService>();
            await service.CreateManyToManyRelationshipAsync(
                Profile, Entity1, Entity2, Name, DisplayName, CancellationToken.None
            ).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or NotSupportedException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "environment entity relationship create failed");
            return 1;
        }

        OutputWriter.WriteLine($"Many-to-many relationship '{Name}' created between '{Entity1}' and '{Entity2}'.");
        return 0;
    }
}
