using System.Text.Json;
using TALXIS.CLI.Platform.PowerPlatform.Control.PowerAutomate;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Connector;

public class OperationSchemaReaderTests
{
    [Fact]
    public void Read_ExtractsParameterNamesTypesAndDescriptions()
    {
        var operation = Read("""
            {
              "properties": {
                "summary": "Post a message",
                "inputsDefinition": {
                  "required": [ "recipient/to" ],
                  "properties": {
                    "recipient/to": { "type": "string", "x-ms-summary": "Recipient" },
                    "body": { "type": "string", "description": "Message body" }
                  }
                }
              }
            }
            """);

        Assert.Equal("Post a message", operation.Summary);
        Assert.Equal(2, operation.Parameters.Count);

        var recipient = Assert.Single(operation.Parameters, p => p.Name == "recipient/to");
        Assert.Equal("string", recipient.Type);
        Assert.True(recipient.Required);
        Assert.Equal("Recipient", recipient.Description);

        Assert.False(Assert.Single(operation.Parameters, p => p.Name == "body").Required);
    }

    [Fact]
    public void Read_HonoursRequirednessDeclaredOnTheParameterItself()
    {
        // Some payloads mark requiredness per parameter rather than in a
        // "required" array on the parent.
        var operation = Read("""
            {
              "properties": {
                "inputsDefinition": {
                  "parameters": { "id": { "type": "string", "required": true } }
                }
              }
            }
            """);

        Assert.True(Assert.Single(operation.Parameters).Required);
    }

    [Fact]
    public void Read_NormalisesEnumValuesAndDefaultsToStrings()
    {
        var operation = Read("""
            {
              "properties": {
                "inputsDefinition": {
                  "properties": {
                    "importance": { "type": "string", "enum": [ "Normal", "High" ], "default": "Normal" },
                    "retries": { "type": "integer", "enum": [ 1, 2, 3 ], "default": 1 }
                  }
                }
              }
            }
            """);

        var importance = Assert.Single(operation.Parameters, p => p.Name == "importance");
        Assert.Equal(["Normal", "High"], importance.AllowedValues);
        Assert.Equal("Normal", importance.DefaultValue);

        var retries = Assert.Single(operation.Parameters, p => p.Name == "retries");
        Assert.Equal(["1", "2", "3"], retries.AllowedValues);
        Assert.Equal("1", retries.DefaultValue);
    }

    [Fact]
    public void Read_ProjectsDynamicValueAndTreeResolvers()
    {
        var operation = Read("""
            {
              "properties": {
                "inputsDefinition": {
                  "properties": {
                    "groupId": {
                      "type": "string",
                      "x-ms-dynamic-values": { "operationId": "GetAllTeams", "parameters": {} }
                    },
                    "folder": {
                      "type": "string",
                      "x-ms-dynamic-tree": { "open": { "operationId": "ListFolders", "parameters": { "id": "root" } } }
                    }
                  }
                }
              }
            }
            """);

        var group = Assert.Single(operation.Parameters, p => p.Name == "groupId");
        Assert.Equal("GetAllTeams", group.DynamicValues?.OperationId);

        var folder = Assert.Single(operation.Parameters, p => p.Name == "folder");
        Assert.Equal("ListFolders", folder.DynamicTree?.OperationId);
        Assert.Equal("root", folder.DynamicTree?.Parameters?["id"]);
    }

    [Theory]
    [InlineData("PostMessageToConversation", null, OperationSchemaReader.OpenApiConnection)]
    [InlineData("PostMessageToConversation", "OpenApiConnectionWebhook", OperationSchemaReader.OpenApiConnectionWebhook)]
    [InlineData("PostMessageToConversation", "OpenApiConnectionNotification", OperationSchemaReader.OpenApiConnectionNotification)]
    // No annotation, but the name says it waits — the Approvals case.
    [InlineData("StartAndWaitForAnApproval", null, OperationSchemaReader.OpenApiConnectionWebhook)]
    [InlineData("WaitForAnApprovalResponse", null, OperationSchemaReader.OpenApiConnectionWebhook)]
    [InlineData("SubscribeToChanges", null, OperationSchemaReader.OpenApiConnectionWebhook)]
    public void Read_InfersTheActionTypeAFlowMustDeclare(string operationId, string? family, string expected)
    {
        var annotation = family is null ? string.Empty : $$"""  "annotation": { "family": "{{family}}" }, """;
        var operation = Read($$"""
            { "properties": { {{annotation}} "inputsDefinition": { "properties": {} } } }
            """, operationId);

        Assert.Equal(expected, operation.ActionType);
    }

    [Fact]
    public void Read_AnAnnotationBeatsTheNamePattern()
    {
        // An operation named like a webhook but annotated as a plain connection
        // must follow the annotation, which is authoritative.
        var operation = Read("""
            { "properties": { "annotation": { "family": "OpenApiConnection" }, "inputsDefinition": {} } }
            """, "StartAndWaitForAnApproval");

        Assert.Equal(OperationSchemaReader.OpenApiConnection, operation.ActionType);
    }

    [Fact]
    public void Read_CapturesTheResponseSchema()
    {
        var operation = Read("""
            {
              "properties": {
                "inputsDefinition": {},
                "responsesDefinition": { "200": { "schema": { "type": "object" } } }
              }
            }
            """);

        Assert.NotNull(operation.ResponseSchema);
        Assert.True(operation.ResponseSchema!.Value.TryGetProperty("200", out _));
    }

    [Fact]
    public void Read_ToleratesAnOperationWithNoInputs()
    {
        var operation = Read("""{ "properties": { "summary": "Ping" } }""");

        Assert.Empty(operation.Parameters);
        Assert.Null(operation.ResponseSchema);
    }

    private static Core.Platforms.PowerPlatform.OperationDetail Read(
        string payload, string operationId = "PostMessageToConversation")
    {
        using var document = JsonDocument.Parse(payload);
        return OperationSchemaReader.Read("shared_teams", operationId, document.RootElement);
    }
}
