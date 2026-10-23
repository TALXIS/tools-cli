using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Features.Environment.Connector.Operation;

/// <summary>Text rendering of an operation spec, shared by 'operation get' and 'operation resolve'.</summary>
internal static class OperationTextRenderer
{
    // Text-renderer callbacks invoked by OutputFormatter — OutputWriter usage is intentional.
#pragma warning disable TXC003
    public static void Print(OperationDetail operation)
    {
        OutputWriter.WriteLine($"{operation.OperationId} ({operation.ApiId})");
        if (!string.IsNullOrWhiteSpace(operation.Summary))
            OutputWriter.WriteLine(operation.Summary);
        OutputWriter.WriteLine($"Action type: {operation.ActionType}");
        if (operation.IsDeprecated)
            OutputWriter.WriteLine("DEPRECATED: use the current replacement operation.");
        OutputWriter.WriteLine(string.Empty);

        if (operation.Parameters.Count == 0) { OutputWriter.WriteLine("This operation takes no parameters."); return; }

        var nameWidth = Math.Max(operation.Parameters.Max(p => p.Name.Length), 20);
        var indent = new string(' ', nameWidth);
        var header = $"{"Parameter".PadRight(nameWidth)} | {"Type".PadRight(8)} | Req | Description";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var p in operation.Parameters)
        {
            OutputWriter.WriteLine($"{p.Name.PadRight(nameWidth)} | {(p.Type ?? "-").PadRight(8)} | {(p.Required ? "yes" : "no ")} | {p.Description}");

            if (p.AllowedValues is { Count: > 0 } allowed)
                OutputWriter.WriteLine($"{indent} | allowed: {string.Join(", ", allowed)}");

            // Without these the parameter looks free-form when its values, or
            // its whole sub-schema, are resolved by another call.
            Describe("values from", p.DynamicValues);
            Describe("tree from", p.DynamicTree);
            Describe("sub-schema from", p.DynamicSchema);
        }

        void Describe(string label, DynamicValuesRef? reference)
        {
            if (reference is null)
                return;

            var arguments = reference.Parameters is { Count: > 0 } bag
                ? " (" + string.Join(", ", bag.Select(kv => $"{kv.Key}={kv.Value}")) + ")"
                : string.Empty;
            OutputWriter.WriteLine($"{indent} | {label}: {reference.OperationId}{arguments}");
        }
    }
#pragma warning restore TXC003
}
