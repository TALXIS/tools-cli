using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Features.Environment.Connector.Operation;

/// <summary>Text rendering of an operation spec, shared by 'operation get' and 'operation resolve'.</summary>
internal static class OperationTextRenderer
{
    private const int MaxValuesShown = 25;

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
            {
                // Long lists (e.g. every table) are summarised; --format json has them all.
                var shown = allowed.Take(MaxValuesShown)
                    .Select(v => p.ValueLabels?.TryGetValue(v, out var label) == true ? $"{v} ({label})" : v);
                var more = allowed.Count > MaxValuesShown ? $", ... {allowed.Count - MaxValuesShown} more (use --format json)" : string.Empty;
                OutputWriter.WriteLine($"{indent} | allowed: {string.Join(", ", shown)}{more}");
            }

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

    public static void PrintSnippet(System.Text.Json.Nodes.JsonObject snippet)
    {
        OutputWriter.WriteLine(string.Empty);
        OutputWriter.WriteLine("Snippet - merge into the flow's properties.connectionReferences and properties.definition (replace <placeholders>):");
        // Relaxed escaping so <placeholders> and @parameters('...') read as
        // they will be pasted; the JSON output mode keeps the standard encoder.
        using var buffer = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(buffer, new System.Text.Json.JsonWriterOptions
        {
            Indented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            snippet.WriteTo(writer);
        }

        OutputWriter.WriteLine(System.Text.Encoding.UTF8.GetString(buffer.ToArray()));
    }
#pragma warning restore TXC003
}
