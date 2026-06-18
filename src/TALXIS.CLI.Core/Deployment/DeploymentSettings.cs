using System.Text.Json.Serialization;

namespace TALXIS.CLI.Core.Deployment;

/// <summary>
/// Deployment settings in the pac / Power Platform Build Tools JSON format, used to
/// pre-populate connection references and environment variable values during a
/// solution import. Mirrors the file produced by <c>pac solution create-settings</c>.
/// </summary>
public sealed record DeploymentSettings
{
    [JsonPropertyName("ConnectionReferences")]
    public IReadOnlyList<ConnectionReferenceSetting> ConnectionReferences { get; init; } = [];

    [JsonPropertyName("EnvironmentVariables")]
    public IReadOnlyList<EnvironmentVariableSetting> EnvironmentVariables { get; init; } = [];

    /// <summary>True when the file carries neither a connection reference nor an environment variable.</summary>
    public bool IsEmpty => ConnectionReferences.Count == 0 && EnvironmentVariables.Count == 0;
}

/// <summary>A single connection reference entry from a deployment settings file.</summary>
public sealed record ConnectionReferenceSetting
{
    [JsonPropertyName("LogicalName")]
    public string? LogicalName { get; init; }

    [JsonPropertyName("ConnectionId")]
    public string? ConnectionId { get; init; }

    [JsonPropertyName("ConnectorId")]
    public string? ConnectorId { get; init; }
}

/// <summary>A single environment variable entry from a deployment settings file.</summary>
public sealed record EnvironmentVariableSetting
{
    [JsonPropertyName("SchemaName")]
    public string? SchemaName { get; init; }

    [JsonPropertyName("Value")]
    public string? Value { get; init; }
}
