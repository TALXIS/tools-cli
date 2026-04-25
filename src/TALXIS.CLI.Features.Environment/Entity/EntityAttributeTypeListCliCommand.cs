using System.Text.Json;
using DotMake.CommandLine;
using TALXIS.CLI.Core;

namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>
/// Lists all supported attribute types with a brief description.
/// Usage: <c>txc environment entity attribute type list [--json]</c>
/// </summary>
[CliCommand(
    Name = "list",
    Description = "List all supported attribute types."
)]
public class EntityAttributeTypeListCliCommand
{
    [CliOption(Name = "--json", Description = "Emit the list as JSON instead of a text table.", Required = false)]
    public bool Json { get; set; }

    public void Run()
    {
        var types = AttributeTypeRegistry.AllTypes;

        if (Json)
        {
            var payload = types.Select(t => new
            {
                type = t.Name,
                sdkType = t.SdkType,
                description = t.Description,
            });
            OutputWriter.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
            return;
        }

        PrintTypesTable(types);
    }

    private static void PrintTypesTable(IReadOnlyList<AttributeTypeInfo> types)
    {
        int nameWidth = Math.Max("Type".Length, types.Max(t => t.Name.Length));
        int sdkWidth = Math.Max("SDK Type".Length, types.Max(t => t.SdkType.Length));
        int descWidth = Math.Max("Description".Length, types.Max(t => t.Description.Length));

        string header =
            $"{"Type".PadRight(nameWidth)} | " +
            $"{"SDK Type".PadRight(sdkWidth)} | " +
            $"{"Description".PadRight(descWidth)}";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(
            $"{new string('-', nameWidth)}-|-" +
            $"{new string('-', sdkWidth)}-|-" +
            $"{new string('-', descWidth)}");

        foreach (var t in types)
        {
            OutputWriter.WriteLine(
                $"{t.Name.PadRight(nameWidth)} | " +
                $"{t.SdkType.PadRight(sdkWidth)} | " +
                $"{t.Description.PadRight(descWidth)}");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
}
