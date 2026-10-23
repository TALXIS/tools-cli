using System.Text.Json;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Platform.PowerPlatform.Control.PowerAutomate;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Connector;

public class OperationSchemaReaderTests
{
    [Fact]
    public void NestedBodyFields_AreFlattenedToTheKeysFlowDefinitionsUse()
    {
        // A flow writes emailMessage/To, never a nested emailMessage object.
        var operation = Read("""
            { "properties": { "inputsDefinition": {
              "required": [ "emailMessage" ],
              "properties": {
                "emailMessage": {
                  "type": "object",
                  "required": [ "To", "Subject" ],
                  "properties": {
                    "To": { "type": "string", "x-ms-summary": "To" },
                    "Subject": { "type": "string" },
                    "Importance": { "type": "string", "enum": [ "Low", "Normal", "High" ] }
                  }
                }
              }
            } } }
            """);

        Assert.Equal(["emailMessage/To", "emailMessage/Subject", "emailMessage/Importance"],
            operation.Parameters.Select(p => p.Name));
        Assert.True(Param(operation, "emailMessage/To").Required);
        Assert.False(Param(operation, "emailMessage/Importance").Required);
        Assert.Equal(["Low", "Normal", "High"], Param(operation, "emailMessage/Importance").AllowedValues);
    }

    [Fact]
    public void ChildOfAnOptionalObject_IsNotRequired()
    {
        var operation = Read("""
            { "properties": { "inputsDefinition": { "properties": {
              "options": { "type": "object", "required": [ "mode" ], "properties": { "mode": { "type": "string" } } }
            } } } }
            """);

        Assert.False(Param(operation, "options/mode").Required);
    }

    [Fact]
    public void DynamicSubSchema_StaysOneParameterCarryingItsResolver()
    {
        // Dataverse CreateRecord: the item columns depend on the chosen table,
        // so the leaf names cannot be read from the operation spec.
        var operation = Read("""
            { "properties": { "inputsDefinition": {
              "required": [ "entityName", "item" ],
              "properties": {
                "entityName": { "type": "string",
                  "x-ms-dynamic-list": { "operationId": "GetEntitySets", "parameters": {} } },
                "item": { "type": "object",
                  "x-ms-dynamic-properties": { "operationId": "GetMetadataForCreate",
                    "parameters": { "entityName": { "parameterReference": "entityName" } } } }
              }
            } } }
            """);

        var item = Param(operation, "item");
        Assert.True(item.Required);
        Assert.Equal("GetMetadataForCreate", item.DynamicSchema!.OperationId);
        Assert.Equal("$ref:entityName", item.DynamicSchema.Parameters!["entityName"]);
        Assert.Equal("GetEntitySets", Param(operation, "entityName").DynamicValues!.OperationId);
    }

    [Fact]
    public void ReadOnlyFields_AreSkipped()
    {
        var operation = Read("""
            { "properties": { "inputsDefinition": { "properties": {
              "id": { "type": "string", "readOnly": true },
              "name": { "type": "string" }
            } } } }
            """);

        Assert.Equal("name", Assert.Single(operation.Parameters).Name);
    }

    [Fact]
    public void RequirednessOnTheParameterItself_IsHonoured()
    {
        var operation = Read("""
            { "properties": { "inputsDefinition": { "parameters": { "id": { "type": "string", "required": true } } } } }
            """);

        Assert.True(Assert.Single(operation.Parameters).Required);
    }

    [Fact]
    public void TreeResolverNestedUnderBrowse_IsRead()
    {
        var operation = Read("""
            { "properties": { "inputsDefinition": { "properties": {
              "folder": { "type": "string", "x-ms-dynamic-tree": { "browse": { "operationId": "ListFolders" } } }
            } } } }
            """);

        Assert.Equal("ListFolders", Param(operation, "folder").DynamicTree!.OperationId);
    }

    [Theory]
    // Seen live: operationType is stated, and family is just the operation's name.
    [InlineData("""{ "operationType": "OpenApiConnectionWebhook", "annotation": { "family": "SubscribeWebhookTrigger" } }""", "SubscribeWebhookTrigger", "OpenApiConnectionWebhook")]
    [InlineData("""{ "annotation": { "family": "OpenApiConnectionWebhook" } }""", "CreateItem", "OpenApiConnectionWebhook")]
    [InlineData("""{ "annotation": { "family": "OpenApiConnection" } }""", "StartAndWaitForAnApproval", "OpenApiConnection")]
    [InlineData("""{}""", "StartAndWaitForAnApproval", "OpenApiConnectionWebhook")]
    [InlineData("""{}""", "ListRecords", "OpenApiConnection")]
    public void ActionType_PrefersTheAnnotationOverTheNamePattern(string properties, string operationId, string expected)
    {
        using var document = JsonDocument.Parse(properties);
        Assert.Equal(expected, OperationSchemaReader.InferActionType(document.RootElement, operationId));
    }

    [Fact]
    public void Detail_CarriesTheApiIdAFlowDefinitionReferences()
    {
        var operation = Read("""{ "properties": { "summary": "List rows", "isDeprecated": true } }""");

        Assert.Equal("/providers/Microsoft.PowerApps/apis/shared_commondataserviceforapps", operation.ApiId);
        Assert.Equal("List rows", operation.Summary);
        Assert.True(operation.IsDeprecated);
        Assert.Empty(operation.Parameters);
    }

    private static OperationDetail Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        return OperationSchemaReader.Read("shared_commondataserviceforapps", "Op", document.RootElement);
    }

    private static OperationParameter Param(OperationDetail operation, string name)
        => Assert.Single(operation.Parameters, p => p.Name == name);
}
