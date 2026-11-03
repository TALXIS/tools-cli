using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Features.Environment.Connector.Operation;

/// <summary>
/// The Dataverse connector's row fields for a table defined in the workspace,
/// built from its <c>Entity.xml</c> instead of a lookup through a connection.
/// Columns added locally but not deployed yet are included.
/// </summary>
/// <remarks>
/// The keys and types follow what the connector itself answers:
/// <list type="bullet">
/// <item>inputs: a column valid for create (<c>CreateRecord</c>, required when the
/// column is business-required) or update (<c>UpdateRecord</c>/<c>UpdateOnlyRecord</c>,
/// never required), a lookup as <c>&lt;column&gt;@odata.bind</c>;</item>
/// <item>outputs: every readable column, a lookup as <c>_&lt;column&gt;_value</c> and its
/// <c>@Microsoft.Dynamics.CRM.lookuplogicalname</c>, plus <c>@odata.id</c>.</item>
/// </list>
/// </remarks>
internal static class DataverseWorkspaceFields
{
    public const string Connector = "shared_commondataserviceforapps";

    private static readonly HashSet<string> CreateOperations = new(StringComparer.OrdinalIgnoreCase) { "CreateRecord" };
    private static readonly HashSet<string> UpdateOperations = new(StringComparer.OrdinalIgnoreCase) { "UpdateRecord", "UpdateOnlyRecord" };

    /// <summary>Column types the connector does not offer as row fields.</summary>
    private static readonly HashSet<string> SkippedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "virtual", "partylist", "entityname", "managedproperty", "calendarrules", "file", "image",
    };

    /// <summary>
    /// The fields of <paramref name="target"/>, or null when they can't be
    /// built locally: another connector or operation, no <c>entityName</c>,
    /// or a table the workspace does not define.
    /// </summary>
    public static LocalFields? TryRead(
        string workspaceRoot, OperationDetail operation, OperationParameter target, bool isOutput, IReadOnlyDictionary<string, string> values)
    {
        if (!string.Equals(ConnectorIds.ToShort(operation.ApiId), Connector, StringComparison.OrdinalIgnoreCase)
            || target.DynamicSchema is null
            || !values.TryGetValue("entityName", out var entityName)
            || (!isOutput && !CreateOperations.Contains(operation.OperationId) && !UpdateOperations.Contains(operation.OperationId)))
        {
            return null;
        }

        if (WorkspaceTableReader.Find(workspaceRoot, entityName) is not { } table)
            return null;

        var file = Path.GetRelativePath(workspaceRoot, table.Files[0]).Replace('\\', '/');
        var columns = table.Columns.Where(c => !SkippedTypes.Contains(c.Type)).ToList();
        return isOutput
            ? new LocalFields(file, null, OutputViews.From(Outputs(operation, target, columns)))
            : new LocalFields(file, ParameterViews.From(Inputs(workspaceRoot, operation, target, columns)), null);
    }

    private static OperationDetail Inputs(string workspaceRoot, OperationDetail operation, OperationParameter target, List<WorkspaceColumn> columns)
    {
        var creating = CreateOperations.Contains(operation.OperationId);
        var parameters = columns
            .Where(c => creating ? c.ValidForCreate : c.ValidForUpdate)
            .Select(c =>
            {
                var lookup = IsLookup(c);
                var options = c.Type is "picklist" or "state" or "status" ? c.Options : null;
                return new OperationParameter(
                    Name: $"{target.Name}/{c.LogicalName}{(lookup ? "@odata.bind" : string.Empty)}",
                    Type: lookup ? "string" : JsonType(c.Type),
                    Required: creating && string.Equals(c.RequiredLevel, "required", StringComparison.OrdinalIgnoreCase),
                    Description: lookup ? LookupDescription(workspaceRoot, c) : c.DisplayName,
                    AllowedValues: options?.Select(o => o.Value).ToList(),
                    DefaultValue: null,
                    DynamicValues: null,
                    DynamicTree: null,
                    DynamicSchema: null,
                    ValueLabels: options?.Where(o => o.Label is not null).ToDictionary(o => o.Value, o => o.Label!));
            })
            .OrderBy(p => p.Description ?? p.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

        return operation with { Parameters = parameters };
    }

    private static OperationDetail Outputs(OperationDetail operation, OperationParameter target, List<WorkspaceColumn> columns)
    {
        // Rows of a list are array items, read inside a loop; one row is read directly.
        var prefix = target.Type == "array" ? target.Name + "[]" : target.Name;
        var outputs = new List<OperationParameter>();
        foreach (var column in columns.Where(c => c.ValidForRead))
        {
            if (IsLookup(column))
            {
                var key = $"{prefix}/_{column.LogicalName}_value";
                outputs.Add(Output(key, "string", $"{column.DisplayName ?? column.LogicalName} (Value)"));
                outputs.Add(Output(key + "@Microsoft.Dynamics.CRM.lookuplogicalname", "string", $"{column.DisplayName ?? column.LogicalName} (Type)"));
            }
            else
            {
                outputs.Add(Output($"{prefix}/{column.LogicalName}", JsonType(column.Type), column.DisplayName));
            }
        }

        // Every row has these, though no Entity.xml declares them.
        outputs.Add(Output($"{prefix}/@odata.id", "string", "OData Id"));
        outputs.Add(Output($"{prefix}/versionnumber", "integer", "Version Number"));
        return operation with
        {
            Outputs = outputs.OrderBy(o => o.Description ?? o.Name, StringComparer.OrdinalIgnoreCase).ThenBy(o => o.Name, StringComparer.Ordinal).ToList(),
        };
    }

    private static OperationParameter Output(string key, string type, string? description)
        => new(key, type, false, description, null, null, null, null, null);

    private static bool IsLookup(WorkspaceColumn column) => column.Type is "lookup" or "owner" or "customer";

    /// <summary>The column's label with the table it points at, as the connector labels it (<c>Location (Warehouse Locations)</c>).</summary>
    private static string? LookupDescription(string workspaceRoot, WorkspaceColumn column)
    {
        var target = column.Type == "owner" ? "Owners"
            : column.Target is { } table ? WorkspaceTableReader.CollectionName(workspaceRoot, table)
            : null;
        return target is null ? column.DisplayName : $"{column.DisplayName ?? column.LogicalName} ({target})";
    }

    /// <summary>The JSON type the connector gives a column of this Dataverse type.</summary>
    private static string JsonType(string type)
        => type switch
        {
            "int" or "integer" or "bigint" or "picklist" or "state" or "status" => "integer",
            "decimal" or "money" or "float" or "double" => "number",
            "bit" or "boolean" => "boolean",
            _ => "string",
        };
}

/// <summary>Fields built from the workspace: parameter fields or output fields, and the file they came from.</summary>
internal sealed record LocalFields(string File, IReadOnlyList<ParameterView>? Parameters, IReadOnlyList<OutputView>? Outputs);
