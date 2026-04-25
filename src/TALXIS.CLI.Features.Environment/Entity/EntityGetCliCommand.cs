using System.Text.Json;
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
/// Retrieves entity-level metadata for a specific entity (table).
/// Usage: <c>txc environment entity get &lt;entity&gt; [--json]</c>
/// </summary>
[CliCommand(
    Name = "get",
    Description = "Get entity-level metadata for an entity."
)]
public class EntityGetCliCommand : ProfiledCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(EntityGetCliCommand));

    [CliArgument(Name = "entity", Description = "The logical name of the entity.")]
    public string Entity { get; set; } = null!;

    [CliOption(Name = "--json", Description = "Emit the result as indented JSON instead of a text layout.", Required = false)]
    public bool Json { get; set; }

    public async Task<int> RunAsync()
    {
        EntityDetailRecord detail;
        try
        {
            var service = TxcServices.Get<IDataverseEntityMetadataService>();
            detail = await service.GetEntityDetailAsync(Profile, Entity, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or NotSupportedException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "environment entity get failed");
            return 1;
        }

        if (Json)
        {
            OutputWriter.WriteLine(JsonSerializer.Serialize(detail, JsonOptions));
            return 0;
        }

        PrintDetail(detail);
        return 0;
    }

    private static void PrintDetail(EntityDetailRecord d)
    {
        const int labelWidth = -23;

        OutputWriter.WriteLine($"{"Logical Name:",labelWidth}{d.LogicalName}");
        OutputWriter.WriteLine($"{"Schema Name:",labelWidth}{d.SchemaName}");
        OutputWriter.WriteLine($"{"Display Name:",labelWidth}{d.DisplayName ?? "-"}");
        OutputWriter.WriteLine($"{"Plural Display Name:",labelWidth}{d.PluralDisplayName ?? "-"}");
        OutputWriter.WriteLine($"{"Description:",labelWidth}{d.Description ?? "-"}");
        OutputWriter.WriteLine($"{"Entity Type Code:",labelWidth}{d.EntityTypeCode?.ToString() ?? "-"}");
        OutputWriter.WriteLine($"{"Ownership:",labelWidth}{d.OwnershipType}");
        OutputWriter.WriteLine($"{"Primary ID Field:",labelWidth}{d.PrimaryIdAttribute ?? "-"}");
        OutputWriter.WriteLine($"{"Primary Name Field:",labelWidth}{d.PrimaryNameAttribute ?? "-"}");
        OutputWriter.WriteLine($"{"Entity Set Name:",labelWidth}{d.EntitySetName ?? "-"}");
        OutputWriter.WriteLine($"{"Collection Schema:",labelWidth}{d.CollectionSchemaName ?? "-"}");
        OutputWriter.WriteLine($"{"Is Custom:",labelWidth}{BoolStr(d.IsCustomEntity)}");
        OutputWriter.WriteLine($"{"Is Activity:",labelWidth}{BoolStr(d.IsActivity)}");
        OutputWriter.WriteLine($"{"Audit Enabled:",labelWidth}{BoolStr(d.IsAuditEnabled)}");
        OutputWriter.WriteLine($"{"Change Tracking:",labelWidth}{BoolStr(d.ChangeTrackingEnabled)}");
        OutputWriter.WriteLine($"{"Is Customizable:",labelWidth}{BoolStr(d.IsCustomizable)}");
    }

    private static string BoolStr(bool value) => value ? "true" : "false";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
}
