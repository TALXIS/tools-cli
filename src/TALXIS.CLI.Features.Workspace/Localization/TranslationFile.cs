using System.Text.Json.Serialization;

namespace TALXIS.CLI.Features.Workspace.Localization;

public sealed class TranslationFile
{
    [JsonPropertyName("sourceLanguage")]
    public string SourceLanguage { get; set; } = string.Empty;

    [JsonPropertyName("targetLanguage")]
    public string TargetLanguage { get; set; } = string.Empty;

    [JsonPropertyName("generatedAt")]
    public string? GeneratedAt { get; set; }

    [JsonPropertyName("workspace")]
    public string? Workspace { get; set; }

    [JsonPropertyName("strings")]
    public List<TranslationUnit> Strings { get; set; } = new();
}

public sealed class TranslationUnit
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("file")]
    public string File { get; set; } = string.Empty;

    [JsonPropertyName("xpath")]
    public string XPath { get; set; } = string.Empty;

    [JsonPropertyName("languageAttr")]
    public string LanguageAttr { get; set; } = "languagecode";

    [JsonPropertyName("valueAttr")]
    public string? ValueAttr { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    // Always serialize, even when null, so consumers see an explicit "target": null
    // slot they need to fill in. Without this, the global JsonIgnoreCondition.WhenWritingNull
    // would omit empty targets entirely and confuse the user / LLM.
    [JsonPropertyName("target")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Target { get; set; }
}
