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
/// Updates an existing attribute (column) on a Dataverse entity.
/// Usage: <c>txc environment entity attribute update --entity &lt;name&gt; --name &lt;name&gt; [--display-name &lt;label&gt;] [--description &lt;text&gt;] [--required &lt;none|recommended|required&gt;]</c>
/// </summary>
[CliCommand(
    Name = "update",
    Description = "Update an existing attribute (column) on an entity."
)]
public class EntityAttributeUpdateCliCommand : ProfiledCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(EntityAttributeUpdateCliCommand));

    [CliOption(Name = "--entity", Description = "The logical name of the entity.", Required = true)]
    public string Entity { get; set; } = null!;

    [CliOption(Name = "--name", Description = "The logical name of the attribute to update.", Required = true)]
    public string Name { get; set; } = null!;

    [CliOption(Name = "--display-name", Description = "The new display name (label) for the attribute.", Required = false)]
    public string? DisplayName { get; set; }

    [CliOption(Name = "--description", Description = "The new description for the attribute.", Required = false)]
    public string? Description { get; set; }

    [CliOption(Name = "--required", Description = "The required level: none, recommended, required.", Required = false)]
    public string? Required { get; set; }

    public async Task<int> RunAsync()
    {
        try
        {
            var service = TxcServices.Get<IDataverseEntityMetadataService>();
            await service.UpdateAttributeAsync(
                Profile, Entity, Name, DisplayName, Description, Required, CancellationToken.None
            ).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or ArgumentException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "environment entity attribute update failed");
            return 1;
        }

        OutputWriter.WriteLine($"Attribute '{Name}' on entity '{Entity}' updated successfully.");
        return 0;
    }
}
