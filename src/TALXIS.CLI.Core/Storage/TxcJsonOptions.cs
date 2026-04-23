using System.Text.Json;
using System.Text.Json.Serialization;

namespace TALXIS.CLI.Core.Storage;

/// <summary>
/// Single source of truth for JSON serialization across txc config files:
/// camelCase property naming, kebab-case string enums, ignore unknown fields
/// on read, indented write, preserve extension-data round-trips.
/// </summary>
public static class TxcJsonOptions
{
    public static readonly JsonSerializerOptions Default = BuildOptions();

    private static JsonSerializerOptions BuildOptions()
    {
        var opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        opts.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower));
        opts.Converters.Add(new SecretRefJsonConverter());
        return opts;
    }
}
