using System.Text.Json;
using System.Text.Json.Serialization;
using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Core.Storage;

/// <summary>
/// Serializes <see cref="SecretRef"/> as its canonical URI string form.
/// </summary>
internal sealed class SecretRefJsonConverter : JsonConverter<SecretRef>
{
    public override SecretRef? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        var raw = reader.GetString();
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return SecretRef.Parse(raw);
    }

    public override void Write(Utf8JsonWriter writer, SecretRef value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Uri);
}
