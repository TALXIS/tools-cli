using System.Text.Json;
using System.Text.Json.Serialization;

namespace TALXIS.CLI.Core;

/// <summary>
/// Single source of truth for JSON serialization of command output (stdout).
/// All commands that emit JSON must use <see cref="Default"/> — do not create
/// local <see cref="JsonSerializerOptions"/> instances in command code.
/// <para>
/// This is distinct from <c>TxcJsonOptions.Default</c> (in Storage namespace)
/// which is used for config file persistence and includes storage-specific
/// converters like <c>SecretRefJsonConverter</c>.
/// </para>
/// </summary>
public static class TxcOutputJsonOptions
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
        };
        opts.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower));
        return opts;
    }
}
