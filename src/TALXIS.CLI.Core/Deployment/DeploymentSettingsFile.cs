using System.Text.Json;

namespace TALXIS.CLI.Core.Deployment;

/// <summary>
/// Loads and parses deployment settings JSON files.
/// </summary>
public static class DeploymentSettingsFile
{
    // The deployment settings format is fixed PascalCase (pac CLI), so the shared
    // camelCase TxcJsonOptions don't apply here - a dedicated case-insensitive reader
    // is intentional and accepts both PascalCase and camelCase files.
#pragma warning disable RS0030 // Bespoke options required: case-insensitive, not camelCase
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
#pragma warning restore RS0030

    /// <summary>Reads and parses a file; returns false with an error message instead of throwing.</summary>
    public static bool TryLoad(string path, out DeploymentSettings? settings, out string? error)
    {
        try
        {
            settings = Load(path);
            error = null;

            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or ArgumentException)
        {
            settings = null;
            error = ex.Message;
            
            return false;
        }
    }

    /// <summary>
    /// Reads and parses a deployment settings file from disk.
    /// </summary>
    public static DeploymentSettings Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Deployment settings file not found: {path}", path);

        return Parse(File.ReadAllText(path));
    }

    /// <summary>
    /// Parses deployment settings from a JSON string.
    /// </summary>
    public static DeploymentSettings Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        DeploymentSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<DeploymentSettings>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Deployment settings file is not valid JSON: {ex.Message}", ex);
        }

        if (settings is null)
            throw new InvalidDataException("Deployment settings file is empty or contains only 'null'.");

        // Reject entries missing their key identifier early — these are almost always a
        // malformed file and otherwise surface as a cryptic failure at import time.
        foreach (var connection in settings.ConnectionReferences)
        {
            if (string.IsNullOrWhiteSpace(connection.LogicalName))
                throw new InvalidDataException("A 'ConnectionReferences' entry is missing its 'LogicalName'.");
        }

        foreach (var variable in settings.EnvironmentVariables)
        {
            if (string.IsNullOrWhiteSpace(variable.SchemaName))
                throw new InvalidDataException("An 'EnvironmentVariables' entry is missing its 'SchemaName'.");
        }

        return settings;
    }
}
