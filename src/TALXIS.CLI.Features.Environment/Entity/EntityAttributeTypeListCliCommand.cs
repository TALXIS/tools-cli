using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>
/// Lists all supported attribute types with a brief description.
/// Usage: <c>txc environment entity attribute type list [--format json]</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List all supported attribute types."
)]
#pragma warning disable TXC003
public class EntityAttributeTypeListCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EntityAttributeTypeListCliCommand));

    protected override Task<int> ExecuteAsync()
    {
        var types = AttributeTypeRegistry.AllTypes;
        OutputFormatter.WriteList(types, PrintTypesTable);
        return Task.FromResult(ExitSuccess);
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

}
