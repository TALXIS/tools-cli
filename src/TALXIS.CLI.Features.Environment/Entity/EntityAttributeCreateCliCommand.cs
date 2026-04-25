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
/// Creates an attribute (column) on a Dataverse entity.
/// Usage: <c>txc environment entity attribute create --entity &lt;name&gt; --name &lt;schema-name&gt; --type &lt;type&gt; [--display-name &lt;label&gt;] [--target-entity &lt;name&gt;] [--options &lt;csv&gt;] [--required]</c>
/// </summary>
[CliCommand(
    Name = "create",
    Description = "Create an attribute (column) on an entity."
)]
public class EntityAttributeCreateCliCommand : ProfiledCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(EntityAttributeCreateCliCommand));

    [CliOption(Name = "--entity", Description = "The logical name of the entity to add the attribute to.", Required = true)]
    public string Entity { get; set; } = null!;

    [CliOption(Name = "--name", Description = "The schema name of the new attribute.", Required = true)]
    public string Name { get; set; } = null!;

    [CliOption(Name = "--type", Description = "The attribute type: lookup, choice, multichoice, string, number, money, bool, datetime, decimal, float, image, file.", Required = true)]
    public string Type { get; set; } = null!;

    [CliOption(Name = "--display-name", Description = "The display name (label) for the attribute. Defaults to the schema name.", Required = false)]
    public string? DisplayName { get; set; }

    [CliOption(Name = "--target-entity", Description = "The target entity for lookup attributes.", Required = false)]
    public string? TargetEntity { get; set; }

    [CliOption(Name = "--options", Description = "Comma-separated option labels for choice/multichoice attributes.", Required = false)]
    public string? Options { get; set; }

    [CliOption(Name = "--required", Description = "Mark the attribute as application-required.", Required = false)]
    public bool Required { get; set; }

    public async Task<int> RunAsync()
    {
        var displayName = DisplayName ?? Name;
        var optionLabels = Options?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        try
        {
            var service = TxcServices.Get<IDataverseEntityMetadataService>();
            await service.CreateAttributeAsync(
                Profile, Entity, Name, displayName, Type, Required, TargetEntity, optionLabels, CancellationToken.None
            ).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "environment entity attribute create failed");
            return 1;
        }

        OutputWriter.WriteLine($"Attribute '{Name}' ({Type}) created on entity '{Entity}'.");
        return 0;
    }
}
