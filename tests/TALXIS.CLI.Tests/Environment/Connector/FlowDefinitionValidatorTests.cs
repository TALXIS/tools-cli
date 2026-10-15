using System.Text.Json;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Platform.PowerPlatform.Control.PowerAutomate;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Connector;

/// <summary>
/// Each test names the authoring mistake it catches. These are the mistakes an
/// AI makes when expanding a scaffolded flow definition by hand — inventing an
/// operation, misspelling a parameter, guessing an enum value, or declaring the
/// wrong action type — which is the reason this validator exists.
/// </summary>
public class FlowDefinitionValidatorTests
{
    private const string TeamsConnector = "shared_teams";
    private const string ApprovalsConnector = "shared_approvals";

    [Fact]
    public async Task CleanDefinition_HasNoErrors()
    {
        var report = await ValidateAsync(BuildFlow());

        Assert.True(report.Valid);
        Assert.Equal(0, report.ErrorCount);
    }

    [Fact]
    public async Task InventedConnector_IsReported()
    {
        var report = await ValidateAsync(BuildFlow(apiId: "/providers/Microsoft.PowerApps/apis/shared_notareal"));

        AssertError(report, "unknown-connector");
    }

    [Fact]
    public async Task InventedOperation_IsReported()
    {
        var report = await ValidateAsync(BuildFlow(operationId: "PostMessageToChannelV9"));

        AssertError(report, "unknown-operation");
    }

    [Fact]
    public async Task MisspelledParameterName_IsReported()
    {
        // "recipient" instead of the real "recipient/to".
        var report = await ValidateAsync(BuildFlow(parameters: """
            { "recipient": "someone@example.com", "body": "hi" }
            """));

        var finding = AssertError(report, "unknown-parameter");
        Assert.Contains("recipient", finding.Message, StringComparison.Ordinal);
        // The message must list the real names so the mistake is fixable.
        Assert.Contains("recipient/to", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingRequiredParameter_IsReported()
    {
        var report = await ValidateAsync(BuildFlow(parameters: """
            { "body": "hi" }
            """));

        var finding = AssertError(report, "missing-required-parameter");
        Assert.Contains("recipient/to", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValueOutsideEnum_IsReported()
    {
        var report = await ValidateAsync(BuildFlow(parameters: """
            { "recipient/to": "someone@example.com", "body": "hi", "importance": "Urgent" }
            """));

        var finding = AssertError(report, "value-not-allowed");
        Assert.Contains("Normal", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExpressionValue_IsNotCheckedAgainstEnum()
    {
        // A runtime expression cannot be resolved here, so it must not be
        // reported as an invalid enum value.
        var report = await ValidateAsync(BuildFlow(parameters: """
            { "recipient/to": "someone@example.com", "body": "hi", "importance": "@variables('level')" }
            """));

        Assert.True(report.Valid);
    }

    [Fact]
    public async Task WebhookOperationDeclaredAsPlainConnection_IsReported()
    {
        // The Approvals case: accepted on save, then the flow never resumes.
        var flow = BuildFlow(
            type: "OpenApiConnection",
            apiId: "/providers/Microsoft.PowerApps/apis/shared_approvals",
            operationId: "StartAndWaitForAnApproval",
            connectionName: "shared_approvals",
            parameters: """{ "approvalType": "Basic" }""");

        var finding = AssertError(await ValidateAsync(flow), "wrong-action-type");
        Assert.Contains("OpenApiConnectionWebhook", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeprecatedOperation_IsAWarningNotAnError()
    {
        var report = await ValidateAsync(BuildFlow(
            operationId: "PostMessageToChannel",
            parameters: """{ "recipient/to": "someone@example.com", "body": "hi" }"""));

        Assert.True(report.Valid);
        Assert.Contains(report.Findings, f => f.Code == "deprecated-operation");
    }

    [Fact]
    public async Task MissingDefinitionParameters_AreReported()
    {
        var report = await ValidateAsync(BuildFlow(includeDefinitionParameters: false));

        var codes = report.Findings.Select(f => f.Code).ToList();
        Assert.Contains("missing-definition-parameter", codes);
        Assert.Equal(2, report.Findings.Count(f => f.Code == "missing-definition-parameter"));
    }

    [Fact]
    public async Task ExplicitAuthenticationInInputs_IsReported()
    {
        var report = await ValidateAsync(BuildFlow(extraInputs: """, "authentication": { "type": "Raw" }"""));

        AssertError(report, "explicit-authentication");
    }

    [Fact]
    public async Task UndeclaredConnectionReference_IsReported()
    {
        // The action points at a connection the definition never declares.
        var report = await ValidateAsync(BuildFlow(
            connectionName: "shared_teams_2", declaredReferenceKey: "shared_teams"));

        AssertError(report, "undeclared-connection-reference");
    }

    [Fact]
    public async Task InvokerConnectionSource_IsReported()
    {
        var report = await ValidateAsync(BuildFlow(connectionSource: "Invoker"));

        AssertError(report, "invalid-connection-source");
    }

    [Fact]
    public async Task MissingTrigger_IsReported()
    {
        var report = await ValidateAsync("""
            { "$schema": "x", "contentVersion": "1.0.0.0", "actions": {} }
            """);

        AssertError(report, "missing-trigger");
    }

    [Fact]
    public async Task ConnectorActionNestedInACondition_IsStillChecked()
    {
        var flow = $$"""
            {
              "properties": {
                "connectionReferences": { "shared_teams": { "connectionName": "shared_teams", "source": "Embedded" } },
                "definition": {
                  "$schema": "x",
                  "contentVersion": "1.0.0.0",
                  "parameters": {
                    "$authentication": { "defaultValue": {}, "type": "SecureObject" },
                    "$connections": { "defaultValue": {}, "type": "Object" }
                  },
                  "triggers": { "manual": { "type": "Request", "kind": "Button" } },
                  "actions": {
                    "Check": {
                      "type": "If",
                      "actions": {
                        "Notify": {
                          "type": "OpenApiConnection",
                          "inputs": {
                            "host": { "apiId": "/providers/Microsoft.PowerApps/apis/{{TeamsConnector}}", "operationId": "NopeNotReal", "connectionName": "shared_teams" },
                            "parameters": {}
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
            """;

        var finding = AssertError(await ValidateAsync(flow), "unknown-operation");
        Assert.Contains("/actions/Check/actions/Notify", finding.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OfflineMode_SkipsMetadataRulesButKeepsStructuralOnes()
    {
        // An invented operation cannot be detected without metadata, but the
        // missing definition parameters still must be.
        var flow = BuildFlow(operationId: "NopeNotReal", includeDefinitionParameters: false);

        var report = await FlowDefinitionValidator.ValidateAsync(
            Parse(flow), lookup: null, connectionCheck: false, connections: [], CancellationToken.None);

        Assert.Contains(report.Findings, f => f.Code == "missing-definition-parameter");
        Assert.DoesNotContain(report.Findings, f => f.Code == "unknown-operation");
    }

    [Fact]
    public async Task BareDefinitionWithoutEnvelope_IsRead()
    {
        var bare = """
            {
              "$schema": "x",
              "contentVersion": "1.0.0.0",
              "triggers": { "manual": { "type": "Request", "kind": "Button" } },
              "actions": {}
            }
            """;

        var report = await ValidateAsync(bare);

        Assert.True(report.Valid);
    }

    [Fact]
    public async Task ClientDataStoredAsAnEscapedString_IsRead()
    {
        var inner = """
            {"properties":{"definition":{"$schema":"x","contentVersion":"1.0.0.0","triggers":{"manual":{"type":"Request","kind":"Button"}},"actions":{}}}}
            """;
        var wrapper = JsonSerializer.Serialize(new Dictionary<string, string> { ["clientdata"] = inner });

        var report = await ValidateAsync(wrapper);

        Assert.True(report.Valid);
    }

    [Fact]
    public async Task UnrecognisableDocument_IsReportedRatherThanThrowing()
    {
        var report = await ValidateAsync("""{ "something": "else" }""");

        AssertError(report, "definition-unreadable");
    }

    [Fact]
    public async Task ConnectionCheck_ReportsAReferenceMissingFromTheEnvironment()
    {
        var flow = BuildFlow(logicalName: "talxis_teamsref");

        var report = await FlowDefinitionValidator.ValidateAsync(
            Parse(flow), BuildLookup(), connectionCheck: true, connections: [], CancellationToken.None);

        AssertError(report, "unknown-connection-reference");
    }

    [Fact]
    public async Task ConnectionCheck_PassesWhenTheReferenceExists()
    {
        var flow = BuildFlow(logicalName: "talxis_teamsref");
        var connections = new[]
        {
            new FlowConnectionSummary("c1", "/providers/Microsoft.PowerApps/apis/shared_teams",
                "Teams", null, "talxis_teamsref", "dataverse"),
        };

        var report = await FlowDefinitionValidator.ValidateAsync(
            Parse(flow), BuildLookup(), connectionCheck: true, connections, CancellationToken.None);

        Assert.True(report.Valid);
    }

    private static Task<FlowValidationReport> ValidateAsync(string json)
        => FlowDefinitionValidator.ValidateAsync(
            Parse(json), BuildLookup(), connectionCheck: false, connections: [], CancellationToken.None);

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static FlowValidationFinding AssertError(FlowValidationReport report, string code)
    {
        var finding = Assert.Single(report.Findings, f => f.Code == code);
        Assert.Equal(FlowValidationSeverity.Error, finding.Severity);
        Assert.False(report.Valid);
        return finding;
    }

    /// <summary>
    /// A minimal but complete flow. Every parameter of this helper exists so a
    /// test can break exactly one thing.
    /// </summary>
    private static string BuildFlow(
        string type = "OpenApiConnection",
        string apiId = "/providers/Microsoft.PowerApps/apis/shared_teams",
        string operationId = "PostMessageToConversation",
        string connectionName = "shared_teams",
        string? declaredReferenceKey = null,
        string connectionSource = "Embedded",
        string? logicalName = null,
        string parameters = """{ "recipient/to": "someone@example.com", "body": "hi" }""",
        string extraInputs = "",
        bool includeDefinitionParameters = true)
    {
        var definitionParameters = includeDefinitionParameters
            ? """
              "parameters": {
                "$authentication": { "defaultValue": {}, "type": "SecureObject" },
                "$connections": { "defaultValue": {}, "type": "Object" }
              },
              """
            : string.Empty;

        var logical = logicalName is null
            ? string.Empty
            : $""", "connectionReferenceLogicalName": "{logicalName}" """;

        // The declared reference key is normally the same as the action's
        // connectionName; a test overrides it to break exactly that link.
        var referenceKey = declaredReferenceKey ?? connectionName;

        return $$"""
            {
              "properties": {
                "connectionReferences": {
                  "{{referenceKey}}": { "connectionName": "{{referenceKey}}", "source": "{{connectionSource}}"{{logical}} }
                },
                "definition": {
                  "$schema": "https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#",
                  "contentVersion": "1.0.0.0",
                  {{definitionParameters}}
                  "triggers": { "manual": { "type": "Request", "kind": "Button" } },
                  "actions": {
                    "Send_a_message": {
                      "type": "{{type}}",
                      "inputs": {
                        "host": { "apiId": "{{apiId}}", "operationId": "{{operationId}}", "connectionName": "{{connectionName}}" },
                        "parameters": {{parameters}}{{extraInputs}}
                      }
                    }
                  }
                }
              }
            }
            """;
    }

    /// <summary>
    /// A stub catalog of two connectors. Anything not listed here does not
    /// exist, which is what the "invented" tests rely on.
    /// </summary>
    private static FlowDefinitionValidator.MetadataLookup BuildLookup()
        => new(
            (connector, _) => Task.FromResult(connector switch
            {
                TeamsConnector => new ConnectorDetail(
                    TeamsConnector, "Microsoft Teams",
                    "/providers/Microsoft.PowerApps/apis/shared_teams", 2, null,
                    [
                        new ConnectorOperationSummary("PostMessageToConversation", "Post a message", "POST", "/v3/beta/teams", 3, false, false),
                        new ConnectorOperationSummary("PostMessageToChannel", "Post a message (deprecated)", "POST", "/beta/teams", 3, true, false),
                    ]),
                ApprovalsConnector => new ConnectorDetail(
                    ApprovalsConnector, "Approvals",
                    "/providers/Microsoft.PowerApps/apis/shared_approvals", 1, null,
                    [
                        new ConnectorOperationSummary("StartAndWaitForAnApproval", "Start and wait", "POST", "/approvals", 1, false, false),
                    ]),
                _ => throw new ArgumentException($"Connector '{connector}' does not exist."),
            }),
            (connector, operation, _) => Task.FromResult((connector, operation) switch
            {
                (TeamsConnector, "PostMessageToConversation") => TeamsPostMessage(operation, deprecated: false),
                (TeamsConnector, "PostMessageToChannel") => TeamsPostMessage(operation, deprecated: true),
                (ApprovalsConnector, "StartAndWaitForAnApproval") => new OperationDetail(
                    ApprovalsConnector, "/providers/Microsoft.PowerApps/apis/shared_approvals",
                    operation, "Start and wait for an approval", null,
                    OperationSchemaReader.OpenApiConnectionWebhook, false,
                    [new OperationParameter("approvalType", "string", true, "Approval type", null, null, null, null, null, null)],
                    null),
                _ => throw new ArgumentException($"Operation '{operation}' does not exist on '{connector}'."),
            }));

    private static OperationDetail TeamsPostMessage(string operationId, bool deprecated)
        => new(
            TeamsConnector,
            "/providers/Microsoft.PowerApps/apis/shared_teams",
            operationId,
            "Post a message",
            null,
            OperationSchemaReader.OpenApiConnection,
            deprecated,
            [
                new OperationParameter("recipient/to", "string", true, "Recipient", null, null, null, null, null, null),
                new OperationParameter("body", "string", false, "Message body", null, null, null, null, null, null),
                new OperationParameter("importance", "string", false, "Importance",
                    ["Normal", "High", "Urgent-High"], "Normal", null, null, null, null),
            ],
            null);
}
