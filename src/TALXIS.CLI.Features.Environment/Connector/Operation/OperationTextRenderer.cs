using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Features.Environment.Connector.Operation;

/// <summary>Text rendering of an operation spec, shared by 'operation get' and 'operation resolve'.</summary>
internal static class OperationTextRenderer
{
    private const int MaxValuesShown = 25;

    // Text-renderer callbacks invoked by OutputFormatter — OutputWriter usage is intentional.
#pragma warning disable TXC003
    public static void Print(OperationSpec spec)
    {
        OutputWriter.WriteLine($"{spec.OperationId} ({spec.ApiId})");
        if (!string.IsNullOrWhiteSpace(spec.Summary))
            OutputWriter.WriteLine(spec.Summary);
        OutputWriter.WriteLine($"{(spec.IsTrigger ? "Trigger" : "Action")} type: {spec.ActionType}");
        if (spec.IsDeprecated)
            OutputWriter.WriteLine("DEPRECATED: use the current replacement operation.");
        OutputWriter.WriteLine(string.Empty);

        if (spec.Parameters.Count == 0) { OutputWriter.WriteLine("This operation takes no parameters."); return; }
        PrintParameters(spec.Parameters);
    }

    public static void PrintParameters(IReadOnlyList<ParameterView> parameters)
    {
        var nameWidth = Math.Max(parameters.Max(p => p.Name.Length), 20);
        var indent = new string(' ', nameWidth);
        var header = $"{"Parameter".PadRight(nameWidth)} | {"Type".PadRight(8)} | Req | Description";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var p in parameters)
        {
            OutputWriter.WriteLine($"{p.Name.PadRight(nameWidth)} | {(p.Type ?? "-").PadRight(8)} | {(p.Required ? "yes" : "no ")} | {p.Description}");

            if (p.AllowedValues is { Count: > 0 } allowed)
            {
                // Long lists (e.g. every table) are summarised; --format json has them all.
                var shown = allowed.Take(MaxValuesShown).Select(v => v.Label is null || v.Label == v.Value ? v.Value : $"{v.Value} ({v.Label})");
                var more = allowed.Count > MaxValuesShown ? $", ... {allowed.Count - MaxValuesShown} more (use --format json)" : string.Empty;
                OutputWriter.WriteLine($"{indent} | allowed: {string.Join(", ", shown)}{more}");
            }

            if (p.ValuesFrom is not null)
            {
                var what = p.ValuesFrom == ParameterSource.Fields ? "sub-fields" : p.ValuesFrom == ParameterSource.List ? "values" : "picker";
                var after = p.DependsOn is { Count: > 0 } d ? $" (after choosing {string.Join(", ", d)})" : string.Empty;
                OutputWriter.WriteLine($"{indent} | {what}{after}: {p.GetValues}");
            }
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
