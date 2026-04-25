using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>
/// Retrieves entity-level metadata for a specific entity (table).
/// Usage: <c>txc environment entity get &lt;entity&gt; [--json]</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "get",
    Description = "Get entity-level metadata for an entity."
)]
#pragma warning disable TXC003
public class EntityGetCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EntityGetCliCommand));

    [CliArgument(Name = "entity", Description = "The logical name of the entity.")]
    public string Entity { get; set; } = null!;

    [CliOption(Name = "--json", Description = "Emit the result as indented JSON instead of a text layout.", Required = false)]
    public bool Json { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        EntityDetailRecord detail;
        try
        {
            var service = TxcServices.Get<IDataverseEntityMetadataService>();
            detail = await service.GetEntityDetailAsync(Profile, Entity, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or NotSupportedException)
        {
            Logger.LogError("{Error}", ex.Message);
            return ExitError;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "environment entity get failed");
            return ExitError;
        }

        if (Json)
        {
            OutputWriter.WriteLine(JsonSerializer.Serialize(detail, JsonOptions));
            return ExitSuccess;
        }

        PrintDetail(detail);
        return ExitSuccess;
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
        OutputWriter.WriteLine($"{"Table Type:",labelWidth}{d.TableType ?? "-"}");
        OutputWriter.WriteLine($"{"Is Custom:",labelWidth}{BoolStr(d.IsCustomEntity)}");
        OutputWriter.WriteLine($"{"Is Activity:",labelWidth}{BoolStr(d.IsActivity)}");
        OutputWriter.WriteLine($"{"Has Notes:",labelWidth}{BoolStr(d.HasNotes)}");
        OutputWriter.WriteLine($"{"Has Activities:",labelWidth}{BoolStr(d.HasActivities)}");
        OutputWriter.WriteLine($"{"Audit Enabled:",labelWidth}{BoolStr(d.IsAuditEnabled)}");
        OutputWriter.WriteLine($"{"Change Tracking:",labelWidth}{BoolStr(d.ChangeTrackingEnabled)}");
        OutputWriter.WriteLine($"{"Is Customizable:",labelWidth}{BoolStr(d.IsCustomizable)}");
    }

    private static string BoolStr(bool value) => value ? "true" : "false";

    private static JsonSerializerOptions JsonOptions => TxcOutputJsonOptions.Default;
}
