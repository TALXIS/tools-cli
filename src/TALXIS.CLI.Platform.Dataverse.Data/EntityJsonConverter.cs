using System.Text.Json;
using Microsoft.Xrm.Sdk;

namespace TALXIS.CLI.Platform.Dataverse.Data;

/// <summary>
/// Converts between <see cref="JsonElement"/> (the CLI/JSON world) and
/// <see cref="Entity"/> (the Dataverse SDK world).
/// </summary>
internal static class EntityJsonConverter
{
    /// <summary>
    /// Builds a Dataverse <see cref="Entity"/> from a flat JSON object.
    /// </summary>
    /// <param name="entityLogicalName">Logical name of the target entity (e.g. <c>account</c>).</param>
    /// <param name="json">A JSON object whose properties map to entity attributes.</param>
    /// <param name="id">Optional explicit record ID; when set, <see cref="Entity.Id"/> is assigned.</param>
    public static Entity JsonToEntity(string entityLogicalName, JsonElement json, Guid? id = null)
    {
        if (json.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Expected a JSON object representing a single record.", nameof(json));

        var entity = new Entity(entityLogicalName);

        if (id.HasValue)
            entity.Id = id.Value;

        foreach (var prop in json.EnumerateObject())
        {
            var value = ConvertJsonValue(prop.Value);
            if (value is null)
                continue;

            // Detect the primary key field by the common Dataverse convention
            // "{entityLogicalName}id" and set Entity.Id instead of passing a
            // raw string — the SDK expects a Guid for primary key attributes.
            bool isPrimaryKey = prop.Name.Equals($"{entityLogicalName}id", StringComparison.OrdinalIgnoreCase);
            if (isPrimaryKey && value is string s && Guid.TryParse(s, out var parsedGuid))
            {
                // When an explicit id was provided by the caller (e.g. record update),
                // validate that the JSON field matches to avoid targeting the wrong record.
                if (id.HasValue && parsedGuid != id.Value)
                    throw new InvalidOperationException(
                        $"The '{prop.Name}' value '{parsedGuid}' in the JSON does not match the explicit record ID '{id.Value}'.");

                entity.Id = parsedGuid;
                entity[prop.Name] = parsedGuid;
            }
            else
            {
                entity[prop.Name] = value;
            }
        }

        return entity;
    }

    /// <summary>
    /// Serializes a Dataverse <see cref="Entity"/> back to a <see cref="JsonElement"/>.
    /// </summary>
    /// <param name="entity">The SDK entity to convert.</param>
    /// <param name="includeAnnotations">
    /// When <c>true</c>, <see cref="Entity.FormattedValues"/> are emitted as
    /// <c>{key}@OData.Community.Display.V1.FormattedValue</c> properties.
    /// </param>
    public static JsonElement EntityToJson(Entity entity, bool includeAnnotations = false)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();

            foreach (var attr in entity.Attributes)
            {
                WriteAttribute(writer, attr.Key, attr.Value);
            }

            if (includeAnnotations)
            {
                foreach (var fv in entity.FormattedValues)
                {
                    writer.WriteString($"{fv.Key}@OData.Community.Display.V1.FormattedValue", fv.Value);
                }
            }

            writer.WriteEndObject();
        }

        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }

    private static object? ConvertJsonValue(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString();

            case JsonValueKind.Number:
                // Try integer types first, then floating-point.
                if (element.TryGetInt32(out var i32)) return i32;
                if (element.TryGetInt64(out var i64)) return i64;
                if (element.TryGetDecimal(out var dec)) return dec;
                return element.GetDouble();

            case JsonValueKind.True:
            case JsonValueKind.False:
                return element.GetBoolean();

            case JsonValueKind.Object:
                // Convention: an object with "Id" + "LogicalName" is an EntityReference.
                if (element.TryGetProperty("Id", out var idProp) &&
                    element.TryGetProperty("LogicalName", out var lnProp) &&
                    lnProp.ValueKind == JsonValueKind.String)
                {
                    var refId = idProp.ValueKind == JsonValueKind.String
                        ? Guid.Parse(idProp.GetString()!)
                        : throw new InvalidOperationException("EntityReference 'Id' must be a string (GUID).");

                    return new EntityReference(lnProp.GetString()!, refId);
                }
                // Unrecognised object — skip rather than throw.
                return null;

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;

            case JsonValueKind.Array:
                // Arrays are not directly supported as attribute values.
                return null;

            default:
                return null;
        }
    }

    private static void WriteAttribute(Utf8JsonWriter writer, string key, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNull(key);
                break;
            case string s:
                writer.WriteString(key, s);
                break;
            case int i:
                writer.WriteNumber(key, i);
                break;
            case long l:
                writer.WriteNumber(key, l);
                break;
            case double d:
                writer.WriteNumber(key, d);
                break;
            case decimal m:
                writer.WriteNumber(key, m);
                break;
            case bool b:
                writer.WriteBoolean(key, b);
                break;
            case DateTime dt:
                writer.WriteString(key, dt);
                break;
            case Guid g:
                writer.WriteString(key, g);
                break;
            case EntityReference er:
                writer.WriteStartObject(key);
                writer.WriteString("Id", er.Id);
                writer.WriteString("LogicalName", er.LogicalName);
                writer.WriteEndObject();
                break;
            case OptionSetValue osv:
                writer.WriteNumber(key, osv.Value);
                break;
            case Money money:
                writer.WriteNumber(key, money.Value);
                break;
            default:
                // Fall back to ToString() for types we don't explicitly handle.
                writer.WriteString(key, value.ToString());
                break;
        }
    }
}
