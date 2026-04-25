using System.Text.Json;

namespace TALXIS.CLI.Core;

/// <summary>
/// Formats and writes command result data to stdout, respecting the active
/// <see cref="OutputContext.Format"/>.
/// <para>
/// <b>This is the only API that command code should use for output.</b>
/// It delegates to <see cref="OutputWriter"/> internally — commands should
/// never call <see cref="OutputWriter"/> directly.
/// </para>
/// <para>
/// In JSON mode (pipes, MCP, <c>--format json</c>): data is serialized with
/// <see cref="TxcOutputJsonOptions.Default"/> — camelCase, indented, null-safe.
/// In text mode (interactive terminals, <c>--format text</c>): data is rendered
/// as human-friendly tables or plain strings.
/// </para>
/// </summary>
public static class OutputFormatter
{
    /// <summary>
    /// Writes a data object. In JSON mode, serializes to JSON.
    /// In text mode, calls the provided <paramref name="textRenderer"/>
    /// to produce human-friendly output.
    /// </summary>
    /// <typeparam name="T">The type of the data object.</typeparam>
    /// <param name="data">The data to output.</param>
    /// <param name="textRenderer">
    /// Optional callback that writes human-friendly output for text mode.
    /// If null and text mode is active, falls back to JSON.
    /// </param>
    public static void WriteData<T>(T data, Action<T>? textRenderer = null)
    {
        if (OutputContext.IsJson || textRenderer == null)
        {
            OutputWriter.WriteLine(JsonSerializer.Serialize(data, TxcOutputJsonOptions.Default));
        }
        else
        {
            textRenderer(data);
        }
    }

    /// <summary>
    /// Writes a pre-serialized JSON string (or raw text) directly.
    /// Use this when the data is already a <see cref="JsonElement"/> or
    /// a serialized JSON string from an external source.
    /// In text mode, calls the provided <paramref name="textRenderer"/>.
    /// </summary>
    public static void WriteRaw(string json, Action? textRenderer = null)
    {
        if (OutputContext.IsJson || textRenderer == null)
        {
            OutputWriter.WriteLine(json);
        }
        else
        {
            textRenderer();
        }
    }

    /// <summary>
    /// Writes a collection as a JSON array (JSON mode) or as a text table (text mode).
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="items">The collection to output.</param>
    /// <param name="tableRenderer">
    /// Callback that renders items as a human-friendly text table.
    /// If null, falls back to JSON.
    /// </param>
    public static void WriteList<T>(IReadOnlyList<T> items, Action<IReadOnlyList<T>>? tableRenderer = null)
    {
        if (OutputContext.IsJson || tableRenderer == null)
        {
            OutputWriter.WriteLine(JsonSerializer.Serialize(items, TxcOutputJsonOptions.Default));
        }
        else
        {
            tableRenderer(items);
        }
    }

    /// <summary>
    /// Writes a single scalar value. In JSON mode, wraps it in a JSON object.
    /// In text mode, writes the plain string.
    /// </summary>
    /// <param name="key">JSON property name (e.g., "id", "path").</param>
    /// <param name="value">The value to output.</param>
    public static void WriteValue(string key, string value)
    {
        if (OutputContext.IsJson)
        {
            var envelope = new Dictionary<string, string> { [key] = value };
            OutputWriter.WriteLine(JsonSerializer.Serialize(envelope, TxcOutputJsonOptions.Default));
        }
        else
        {
            OutputWriter.WriteLine(value);
        }
    }

    /// <summary>
    /// Writes a result envelope for mutative commands (create, update, delete, import).
    /// In text mode, writes a human-friendly message.
    /// </summary>
    /// <param name="status">Outcome status (e.g., "succeeded", "failed").</param>
    /// <param name="message">Human-readable description of what happened.</param>
    /// <param name="id">Optional entity identifier (e.g., created record ID).</param>
    public static void WriteResult(string status, string? message = null, string? id = null)
    {
        if (OutputContext.IsJson)
        {
            var envelope = new CommandResultEnvelope { Status = status, Message = message, Id = id };
            OutputWriter.WriteLine(JsonSerializer.Serialize(envelope, TxcOutputJsonOptions.Default));
        }
        else
        {
            OutputWriter.WriteLine(message ?? status);
        }
    }

    /// <summary>
    /// Writes a dynamic table with string columns (useful for query results
    /// where the schema is not known at compile time).
    /// In JSON mode, passes through the pre-serialized JSON elements.
    /// In text mode, renders a formatted table.
    /// </summary>
    public static void WriteDynamicTable(
        IReadOnlyList<JsonElement> records,
        Action<IReadOnlyList<JsonElement>> tableRenderer)
    {
        if (OutputContext.IsJson)
        {
            OutputWriter.WriteLine(JsonSerializer.Serialize(records, TxcOutputJsonOptions.Default));
        }
        else
        {
            tableRenderer(records);
        }
    }
}

/// <summary>
/// Standard JSON envelope for mutative command results.
/// </summary>
internal sealed class CommandResultEnvelope
{
    public string Status { get; set; } = "succeeded";
    public string? Message { get; set; }
    public string? Id { get; set; }
}
