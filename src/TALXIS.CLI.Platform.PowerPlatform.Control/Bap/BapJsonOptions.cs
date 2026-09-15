using System.Text.Json;
using System.Text.Json.Serialization;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Bap;

/// <summary>
/// JSON serialization options for BAP admin API request bodies: camelCase
/// property names (the API contract) and null omission so optional metadata
/// (domain, security group, templates) is only sent when supplied.
/// </summary>
internal static class BapJsonOptions
{
    public static readonly JsonSerializerOptions Default = BuildOptions();

    private static JsonSerializerOptions BuildOptions()
    {
#pragma warning disable RS0030 // This IS the approved JsonSerializerOptions factory for BAP request bodies
        return new JsonSerializerOptions(JsonSerializerDefaults.Web)
#pragma warning restore RS0030
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }
}
