using System.Text.Json;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.PowerAutomate;

/// <summary>
/// Checks a locally authored flow definition against live connector metadata.
/// </summary>
/// <remarks>
/// Flow definitions are hand-expanded JSON, which makes them the easiest place
/// in a workspace to invent an operation, a parameter name or an action type
/// that does not exist. Every rule here exists to catch one such invention
/// before the solution is deployed. The type is deliberately pure — metadata
/// arrives through <see cref="MetadataLookup"/> — so the rules are testable
/// without any network access.
/// </remarks>
internal static class FlowDefinitionValidator
{
    /// <summary>Action types that bind to a connector operation.</summary>
    private static readonly string[] ConnectorActionTypes =
    [
        OperationSchemaReader.OpenApiConnection,
        OperationSchemaReader.OpenApiConnectionWebhook,
        OperationSchemaReader.OpenApiConnectionNotification,
    ];

    /// <summary>Action types whose nested <c>actions</c> must also be walked.</summary>
    private static readonly string[] ContainerActionTypes = ["if", "foreach", "until", "scope", "switch"];

    /// <summary>
    /// Metadata accessors the validator needs. Supplied by the service in
    /// normal use and stubbed in tests; <see langword="null"/> means offline,
    /// where only structural rules run.
    /// </summary>
    internal sealed record MetadataLookup(
        Func<string, CancellationToken, Task<ConnectorDetail>> GetConnectorAsync,
        Func<string, string, CancellationToken, Task<OperationDetail>> GetOperationAsync);

    public static async Task<FlowValidationReport> ValidateAsync(
        JsonElement document,
        MetadataLookup? lookup,
        bool connectionCheck,
        IReadOnlyList<FlowConnectionSummary> connections,
        CancellationToken ct)
    {
        var findings = new List<FlowValidationFinding>();

        if (!FlowDefinitionReader.TryRead(document, out var flow, out var readError))
        {
            findings.Add(new FlowValidationFinding(
                FlowValidationSeverity.Error, "/", "definition-unreadable", readError!));
            return Report(findings);
        }

        var definition = flow.Definition;

        ValidateEnvelope(definition, findings);

        var actions = new List<FlowAction>();
        CollectActions(definition, "triggers", "/definition/triggers", actions, findings);
        CollectActions(definition, "actions", "/definition/actions", actions, findings);

        var connectorActions = actions.Where(a => IsConnectorAction(a.Type)).ToList();
        if (connectorActions.Count > 0)
            ValidateDefinitionParameters(definition, findings);

        foreach (var action in connectorActions)
        {
            await ValidateConnectorActionAsync(action, flow, lookup, connectionCheck, connections, findings, ct)
                .ConfigureAwait(false);
        }

        return Report(findings);
    }

    private static FlowValidationReport Report(List<FlowValidationFinding> findings)
    {
        var errors = findings.Count(f => f.Severity == FlowValidationSeverity.Error);
        var warnings = findings.Count - errors;
        return new FlowValidationReport(errors == 0, errors, warnings, findings);
    }

    /// <summary>Checks the definition header that the Logic Apps runtime requires.</summary>
    private static void ValidateEnvelope(JsonElement definition, List<FlowValidationFinding> findings)
    {
        if (SwaggerOperationIndexer.TryGetString(definition, "$schema") is null)
        {
            findings.Add(new FlowValidationFinding(
                FlowValidationSeverity.Warning, "/definition/$schema", "missing-schema",
                "The definition declares no $schema. Use the Logic Apps workflow definition schema."));
        }

        if (SwaggerOperationIndexer.TryGetString(definition, "contentVersion") is null)
        {
            findings.Add(new FlowValidationFinding(
                FlowValidationSeverity.Warning, "/definition/contentVersion", "missing-content-version",
                "The definition declares no contentVersion. Use \"1.0.0.0\"."));
        }

        if (SwaggerOperationIndexer.TryGetObject(definition, "triggers") is not { } triggers
            || !triggers.EnumerateObject().Any())
        {
            findings.Add(new FlowValidationFinding(
                FlowValidationSeverity.Error, "/definition/triggers", "missing-trigger",
                "The definition declares no triggers. A flow needs exactly one trigger."));
        }
    }

    /// <summary>
    /// A connector action reads its credentials through these two definition
    /// parameters. Without them the flow saves but cannot authenticate.
    /// </summary>
    private static void ValidateDefinitionParameters(JsonElement definition, List<FlowValidationFinding> findings)
    {
        var parameters = SwaggerOperationIndexer.TryGetObject(definition, "parameters");

        Check("$authentication", "SecureObject");
        Check("$connections", "Object");

        void Check(string name, string expectedType)
        {
            var declared = parameters is { } p ? SwaggerOperationIndexer.TryGetObject(p, name) : null;
            if (declared is null)
            {
                findings.Add(new FlowValidationFinding(
                    FlowValidationSeverity.Error, $"/definition/parameters/{name}", "missing-definition-parameter",
                    $"Connector actions require the '{name}' definition parameter of type {expectedType}."));
                return;
            }

            var type = SwaggerOperationIndexer.TryGetString(declared.Value, "type");
            if (!string.Equals(type, expectedType, StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new FlowValidationFinding(
                    FlowValidationSeverity.Error, $"/definition/parameters/{name}/type", "wrong-parameter-type",
                    $"Definition parameter '{name}' must be of type {expectedType} but is '{type ?? "(none)"}'."));
            }
        }
    }

    private static async Task ValidateConnectorActionAsync(
        FlowAction action,
        FlowDocument flow,
        MetadataLookup? lookup,
        bool connectionCheck,
        IReadOnlyList<FlowConnectionSummary> connections,
        List<FlowValidationFinding> findings,
        CancellationToken ct)
    {
        var inputs = SwaggerOperationIndexer.TryGetObject(action.Element, "inputs");
        var host = inputs is { } i ? SwaggerOperationIndexer.TryGetObject(i, "host") : null;

        if (host is null)
        {
            findings.Add(new FlowValidationFinding(
                FlowValidationSeverity.Error, $"{action.Path}/inputs/host", "missing-host",
                $"Action '{action.Name}' is a {action.Type} but declares no inputs.host."));
            return;
        }

        // The runtime injects authentication on save; declaring it by hand is
        // both unnecessary and rejected.
        if (inputs is { } inputsElement && inputsElement.TryGetProperty("authentication", out _))
        {
            findings.Add(new FlowValidationFinding(
                FlowValidationSeverity.Error, $"{action.Path}/inputs/authentication", "explicit-authentication",
                $"Action '{action.Name}' declares 'authentication' in its inputs. Remove it — the platform injects it on save."));
        }

        var apiId = SwaggerOperationIndexer.TryGetString(host.Value, "apiId");
        var connector = PowerAutomateEndpointProvider.TryParseConnectorFromApiId(apiId);
        var operationId = SwaggerOperationIndexer.TryGetString(host.Value, "operationId");

        if (connector is null)
        {
            findings.Add(new FlowValidationFinding(
                FlowValidationSeverity.Error, $"{action.Path}/inputs/host/apiId", "invalid-api-id",
                $"Action '{action.Name}' has an unusable apiId '{apiId ?? "(none)"}'. " +
                "Expected '/providers/Microsoft.PowerApps/apis/{connector}'."));
            return;
        }

        if (string.IsNullOrWhiteSpace(operationId))
        {
            findings.Add(new FlowValidationFinding(
                FlowValidationSeverity.Error, $"{action.Path}/inputs/host/operationId", "missing-operation-id",
                $"Action '{action.Name}' declares no operationId."));
            return;
        }

        ValidateConnectionBinding(action, host.Value, flow, connectionCheck, connections, findings);

        if (lookup is null)
            return;

        ConnectorDetail connectorDetail;
        try
        {
            connectorDetail = await lookup.GetConnectorAsync(connector, ct).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            findings.Add(new FlowValidationFinding(
                FlowValidationSeverity.Error, $"{action.Path}/inputs/host/apiId", "unknown-connector",
                $"Connector '{connector}' does not exist in this environment."));
            return;
        }

        if (!connectorDetail.Operations.Any(o =>
                string.Equals(o.OperationId, operationId, StringComparison.Ordinal)))
        {
            findings.Add(new FlowValidationFinding(
                FlowValidationSeverity.Error, $"{action.Path}/inputs/host/operationId", "unknown-operation",
                $"Operation '{operationId}' does not exist on connector '{connector}'."));
            return;
        }

        OperationDetail operation;
        try
        {
            operation = await lookup.GetOperationAsync(connector, operationId, ct).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            findings.Add(new FlowValidationFinding(
                FlowValidationSeverity.Error, $"{action.Path}/inputs/host/operationId", "unknown-operation",
                $"Operation '{operationId}' does not exist on connector '{connector}'."));
            return;
        }

        // The single most damaging mismatch: a webhook operation declared as a
        // plain connection is accepted on save and then never resumes.
        if (!string.Equals(action.Type, operation.ActionType, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new FlowValidationFinding(
                FlowValidationSeverity.Error, $"{action.Path}/type", "wrong-action-type",
                $"Action '{action.Name}' uses type '{action.Type}' but operation '{operationId}' requires '{operation.ActionType}'."));
        }

        if (operation.IsDeprecated)
        {
            findings.Add(new FlowValidationFinding(
                FlowValidationSeverity.Warning, $"{action.Path}/inputs/host/operationId", "deprecated-operation",
                $"Operation '{operationId}' on connector '{connector}' is deprecated. Use its current replacement."));
        }

        ValidateParameters(action, inputs, operation, findings);
    }

    private static void ValidateParameters(
        FlowAction action,
        JsonElement? inputs,
        OperationDetail operation,
        List<FlowValidationFinding> findings)
    {
        var supplied = inputs is { } i ? SwaggerOperationIndexer.TryGetObject(i, "parameters") : null;
        var suppliedNames = new HashSet<string>(StringComparer.Ordinal);

        if (supplied is { } parameters)
        {
            foreach (var entry in parameters.EnumerateObject())
            {
                suppliedNames.Add(entry.Name);

                var spec = operation.Parameters.FirstOrDefault(p =>
                    string.Equals(p.Name, entry.Name, StringComparison.Ordinal));

                if (spec is null)
                {
                    findings.Add(new FlowValidationFinding(
                        FlowValidationSeverity.Error,
                        $"{action.Path}/inputs/parameters/{EscapePointer(entry.Name)}",
                        "unknown-parameter",
                        $"Operation '{operation.OperationId}' has no parameter '{entry.Name}'. " +
                        $"Valid names: {DescribeNames(operation)}"));
                    continue;
                }

                ValidateAllowedValue(action, entry, spec, operation, findings);
            }
        }

        foreach (var required in operation.Parameters.Where(p => p.Required))
        {
            if (suppliedNames.Contains(required.Name))
                continue;

            findings.Add(new FlowValidationFinding(
                FlowValidationSeverity.Error,
                $"{action.Path}/inputs/parameters",
                "missing-required-parameter",
                $"Operation '{operation.OperationId}' requires parameter '{required.Name}', which is not supplied."));
        }
    }

    private static void ValidateAllowedValue(
        FlowAction action,
        JsonProperty entry,
        OperationParameter spec,
        OperationDetail operation,
        List<FlowValidationFinding> findings)
    {
        if (spec.AllowedValues is not { Count: > 0 } allowed)
            return;

        // Only literal strings can be checked; an expression is resolved at run
        // time and its value is unknowable here.
        if (entry.Value.ValueKind != JsonValueKind.String)
            return;

        var value = entry.Value.GetString();
        if (value is null || value.StartsWith('@'))
            return;

        if (allowed.Contains(value, StringComparer.Ordinal))
            return;

        findings.Add(new FlowValidationFinding(
            FlowValidationSeverity.Error,
            $"{action.Path}/inputs/parameters/{EscapePointer(entry.Name)}",
            "value-not-allowed",
            $"Parameter '{entry.Name}' of operation '{operation.OperationId}' does not accept '{value}'. " +
            $"Allowed values: {string.Join(", ", allowed)}"));
    }

    /// <summary>
    /// Checks that the action's connection name resolves to a declared
    /// reference, and that the reference binds the way a deployed solution
    /// needs it to.
    /// </summary>
    private static void ValidateConnectionBinding(
        FlowAction action,
        JsonElement host,
        FlowDocument flow,
        bool connectionCheck,
        IReadOnlyList<FlowConnectionSummary> connections,
        List<FlowValidationFinding> findings)
    {
        var connectionName = SwaggerOperationIndexer.TryGetString(host, "connectionName");
        if (string.IsNullOrWhiteSpace(connectionName))
        {
            findings.Add(new FlowValidationFinding(
                FlowValidationSeverity.Error, $"{action.Path}/inputs/host/connectionName", "missing-connection-name",
                $"Action '{action.Name}' declares no connectionName."));
            return;
        }

        if (flow.ConnectionReferences is not { } references)
        {
            findings.Add(new FlowValidationFinding(
                FlowValidationSeverity.Error, "/properties/connectionReferences", "missing-connection-references",
                $"Action '{action.Name}' references connection '{connectionName}', but the flow declares no connectionReferences."));
            return;
        }

        if (SwaggerOperationIndexer.TryGetObject(references, connectionName) is not { } reference)
        {
            findings.Add(new FlowValidationFinding(
                FlowValidationSeverity.Error,
                $"/properties/connectionReferences/{EscapePointer(connectionName)}",
                "undeclared-connection-reference",
                $"Action '{action.Name}' references connection '{connectionName}', which is not declared in connectionReferences."));
            return;
        }

        var source = SwaggerOperationIndexer.TryGetString(reference, "source");
        if (source is not null && !string.Equals(source, "Embedded", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new FlowValidationFinding(
                FlowValidationSeverity.Error,
                $"/properties/connectionReferences/{EscapePointer(connectionName)}/source",
                "invalid-connection-source",
                $"Connection reference '{connectionName}' uses source '{source}'. Use \"Embedded\"."));
        }

        if (!connectionCheck)
            return;

        var logicalName = SwaggerOperationIndexer.TryGetString(reference, "connectionReferenceLogicalName");
        if (string.IsNullOrWhiteSpace(logicalName))
        {
            findings.Add(new FlowValidationFinding(
                FlowValidationSeverity.Warning,
                $"/properties/connectionReferences/{EscapePointer(connectionName)}",
                "missing-connection-reference-logical-name",
                $"Connection reference '{connectionName}' declares no connectionReferenceLogicalName, so it cannot be bound by a solution import."));
            return;
        }

        var exists = connections.Any(c =>
            string.Equals(c.ConnectionReferenceLogicalName, logicalName, StringComparison.OrdinalIgnoreCase));

        if (!exists)
        {
            findings.Add(new FlowValidationFinding(
                FlowValidationSeverity.Error,
                $"/properties/connectionReferences/{EscapePointer(connectionName)}/connectionReferenceLogicalName",
                "unknown-connection-reference",
                $"Connection reference '{logicalName}' does not exist in this environment."));
        }
    }

    /// <summary>
    /// Flattens triggers and actions, descending into container actions so a
    /// connector call nested in a condition or loop is checked too.
    /// </summary>
    private static void CollectActions(
        JsonElement parent,
        string propertyName,
        string path,
        List<FlowAction> actions,
        List<FlowValidationFinding> findings)
    {
        if (SwaggerOperationIndexer.TryGetObject(parent, propertyName) is not { } bag)
            return;

        foreach (var entry in bag.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Object)
                continue;

            var actionPath = $"{path}/{EscapePointer(entry.Name)}";
            var type = SwaggerOperationIndexer.TryGetString(entry.Value, "type");

            if (type is null)
            {
                findings.Add(new FlowValidationFinding(
                    FlowValidationSeverity.Error, $"{actionPath}/type", "missing-action-type",
                    $"'{entry.Name}' declares no type."));
                continue;
            }

            actions.Add(new FlowAction(entry.Name, type, actionPath, entry.Value));

            if (!ContainerActionTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
                continue;

            CollectActions(entry.Value, "actions", $"{actionPath}/actions", actions, findings);

            if (SwaggerOperationIndexer.TryGetObject(entry.Value, "else") is { } elseBranch)
                CollectActions(elseBranch, "actions", $"{actionPath}/else/actions", actions, findings);

            if (SwaggerOperationIndexer.TryGetObject(entry.Value, "cases") is { } cases)
            {
                foreach (var branch in cases.EnumerateObject())
                {
                    CollectActions(
                        branch.Value, "actions",
                        $"{actionPath}/cases/{EscapePointer(branch.Name)}/actions", actions, findings);
                }
            }

            if (SwaggerOperationIndexer.TryGetObject(entry.Value, "default") is { } defaultBranch)
                CollectActions(defaultBranch, "actions", $"{actionPath}/default/actions", actions, findings);
        }
    }

    private static bool IsConnectorAction(string type)
        => ConnectorActionTypes.Contains(type, StringComparer.OrdinalIgnoreCase);

    private static string DescribeNames(OperationDetail operation)
    {
        var names = operation.Parameters.Select(p => p.Name).Take(15).ToList();
        if (names.Count == 0)
            return "(the operation declares no parameters)";

        var rendered = string.Join(", ", names);
        return operation.Parameters.Count > names.Count ? rendered + ", ..." : rendered;
    }

    /// <summary>Escapes a JSON pointer segment per RFC 6901.</summary>
    private static string EscapePointer(string segment)
        => segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private sealed record FlowAction(string Name, string Type, string Path, JsonElement Element);
}
