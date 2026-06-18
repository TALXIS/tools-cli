using TALXIS.CLI.Core.Deployment;

namespace TALXIS.CLI.Platform.Dataverse.Application.Sdk;

/// <summary>A connection reference declared by the solution being imported.</summary>
public sealed record SolutionConnectionReference(string LogicalName, string? ConnectorId);

/// <summary>An environment variable (definition or value) declared by the solution being imported.</summary>
public sealed record SolutionEnvironmentVariable(string SchemaName, string? ValueId);

/// <summary>Connection reference for ComponentParameters. ConnectionId is a string (the column isn't a GUID).</summary>
public sealed record PlannedConnectionReference(string LogicalName, string ConnectionId, string? ConnectorId);

/// <summary>Environment variable value for ComponentParameters.</summary>
public sealed record PlannedEnvironmentVariable(string SchemaName, string Value, string? ValueId);

/// <summary>Values to apply, plus warnings about skipped entries.</summary>
public sealed record ComponentParameterPlan(
    IReadOnlyList<PlannedConnectionReference> ConnectionReferences,
    IReadOnlyList<PlannedEnvironmentVariable> EnvironmentVariables,
    IReadOnlyList<string> Warnings)
{
    public bool IsEmpty => ConnectionReferences.Count == 0 && EnvironmentVariables.Count == 0;
}

/// <summary>Reconciles a deployment settings file with the solution's declared components. Pure, no Dataverse calls.</summary>
public static class ComponentParameterPlanner
{
    public static ComponentParameterPlan Plan(
        DeploymentSettings settings,
        IReadOnlyList<SolutionConnectionReference> solutionConnectionReferences,
        IReadOnlyList<SolutionEnvironmentVariable> solutionEnvironmentVariables)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(solutionConnectionReferences);
        ArgumentNullException.ThrowIfNull(solutionEnvironmentVariables);

        var warnings = new List<string>();
        var connectionReferences = new List<PlannedConnectionReference>();
        var environmentVariables = new List<PlannedEnvironmentVariable>();

        var declaredReferences = solutionConnectionReferences
            .GroupBy(r => r.LogicalName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // A schema name can appear both as a definition and a value component — prefer
        // the value component so we carry its id and update in place rather than duplicate.
        var declaredVariables = solutionEnvironmentVariables
            .GroupBy(v => v.SchemaName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(v => v.ValueId is not null).First(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var setting in settings.ConnectionReferences)
        {
            // LogicalName is guaranteed non-empty by DeploymentSettingsFile.
            var logicalName = setting.LogicalName!;

            if (!declaredReferences.TryGetValue(logicalName, out var declared))
            {
                warnings.Add($"Connection reference '{logicalName}' from the settings file is not part of the solution; skipping.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(setting.ConnectionId))
            {
                warnings.Add($"Connection reference '{logicalName}' has no ConnectionId in the settings file; skipping.");
                continue;
            }

            var connectorId = !string.IsNullOrWhiteSpace(setting.ConnectorId) ? setting.ConnectorId : declared.ConnectorId;
            connectionReferences.Add(new PlannedConnectionReference(logicalName, setting.ConnectionId.Trim(), connectorId));
        }

        foreach (var setting in settings.EnvironmentVariables)
        {
            // SchemaName is guaranteed non-empty by DeploymentSettingsFile.
            var schemaName = setting.SchemaName!;

            if (!declaredVariables.TryGetValue(schemaName, out var declared))
            {
                warnings.Add($"Environment variable '{schemaName}' from the settings file is not part of the solution; skipping.");
                continue;
            }

            // pac create-settings emits empty placeholders for unfilled values; treat
            // those as "not provided" rather than overwriting with a blank value.
            if (string.IsNullOrWhiteSpace(setting.Value))
            {
                warnings.Add($"Environment variable '{schemaName}' has no value in the settings file; skipping.");
                continue;
            }

            environmentVariables.Add(new PlannedEnvironmentVariable(schemaName, setting.Value, declared.ValueId));
        }

        return new ComponentParameterPlan(connectionReferences, environmentVariables, warnings);
    }
}
