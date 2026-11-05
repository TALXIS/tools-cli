using System.Text.Json;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Platform.PowerPlatform.Control.PowerAutomate;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Connector;

public class SwaggerOperationReaderTests
{
    private const string Connector = "lab_connectorsopenfoodfacts";

    private static readonly string OpenFoodFacts = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Environment", "Connector", "Fixtures", "openfoodfacts.swagger.json"));

    private readonly SwaggerOperationReader _reader = new();

    [Fact]
    public void OpenFoodFacts_PathParameterAndRefBody_MatchTheEnvironmentAnswer()
    {
        // The values are what 'operation get' printed for the deployed
        // connector on wm-dev-2102, read from the environment's apiOperations.
        var operation = _reader.ReadOperation(OpenFoodFacts, Connector, "GetProductByBarcode");

        Assert.Equal("GetProductByBarcode", operation.OperationId);
        Assert.Equal(OperationSchemaReader.OpenApiConnection, operation.ActionType);
        Assert.False(operation.IsTrigger);
        Assert.Equal("Get product by barcode", operation.Summary);
        var barcode = Assert.Single(operation.Parameters);
        Assert.Equal(("barcode", "string", true, "Barcode"), (barcode.Name, barcode.Type, barcode.Required, barcode.Description));
        Assert.Equal(
            [("body/ean", "EAN"), ("body/name", "Name"), ("body/brand", "Brand"), ("body/quantity", "Quantity"), ("body/imageUrl", "Image URL")],
            operation.Outputs!.Select(o => (o.Name, o.Description)));
        Assert.All(operation.Outputs!, o => Assert.Equal("string", o.Type));
    }

    [Fact]
    public void OpenFoodFacts_FileResponse_IsTheBodyAsAString()
    {
        var operation = _reader.ReadOperation(OpenFoodFacts, Connector, "GetProductImage");

        var imageUrl = Assert.Single(operation.Parameters);
        Assert.Equal(("imageUrl", true, "Image URL"), (imageUrl.Name, imageUrl.Required, imageUrl.Description));
        var body = Assert.Single(operation.Outputs!);
        Assert.Equal(("body", "string"), (body.Name, body.Type));
    }

    [Fact]
    public void OpenFoodFacts_Index_MatchesTheEnvironmentIndex()
    {
        var connector = _reader.ReadConnector(OpenFoodFacts, Connector, search: null);

        Assert.Equal(Connector, connector.LogicalName);
        Assert.Equal("Open Food Facts", connector.DisplayName);
        Assert.Equal(
            [("GetProductByBarcode", "action", "Get product by barcode"), ("GetProductImage", "action", "Get product image")],
            connector.Operations.Select(o => (o.OperationId, o.Kind, o.Summary)));
        Assert.Equal(1, _reader.ReadConnector(OpenFoodFacts, Connector, search: "image").MatchedOperations);
    }

    [Fact]
    public void OperationId_IsMatchedIgnoringCase_AndAnUnknownOneIsBadInput()
    {
        Assert.Equal("GetProductByBarcode", _reader.ReadOperation(OpenFoodFacts, Connector, "getproductbybarcode").OperationId);
        Assert.Throws<ArgumentException>(() => _reader.ReadOperation(OpenFoodFacts, Connector, "Nope"));
        Assert.Throws<ArgumentException>(() => _reader.ReadOperation("[]", Connector, "Nope"));
        Assert.Throws<ArgumentException>(() => _reader.ReadOperation("{", Connector, "Nope"));
    }

    [Fact]
    public void TeamsLikePostMessage_HasTheParametersTheEnvironmentShapeGives()
    {
        // The same operation in both vocabularies: raw swagger (x-ms-dynamic-values,
        // a $ref'd body with x-ms-dynamic-schema) and the environment's
        // apiOperations payload (x-ms-dynamic-list/-properties).
        var fromSwagger = _reader.ReadOperation(TeamsLike, "shared_teams", "PostMessageToConversation");
        using var environment = JsonDocument.Parse("""
            { "properties": { "summary": "Post message in a chat or channel", "inputsDefinition": {
              "required": [ "poster", "location", "body" ], "properties": {
              "poster": { "type": "string", "x-ms-summary": "Post as", "enum": [ "Flow bot", "User" ] },
              "location": { "type": "string", "x-ms-summary": "Post in", "x-ms-dynamic-list": { "operationId": "GetMessageLocations",
                "itemsPath": "value", "itemValuePath": "id", "itemTitlePath": "displayName",
                "parameters": { "messageType": { "value": "ParentMessage" }, "poster": { "parameterReference": "poster" } } } },
              "body": { "type": "object", "x-ms-dynamic-properties": { "operationId": "GetUnifiedActionSchema", "itemValuePath": "schema",
                "parameters": { "actionType": { "value": "Message" }, "poster": { "parameterReference": "poster" },
                                "recipientType": { "parameterReference": "location" } } } },
              "x-ms-client-request-id": { "type": "string", "x-ms-summary": "Request id" }
            } } } }
            """);
        var fromEnvironment = OperationSchemaReader.Read("shared_teams", "PostMessageToConversation", environment.RootElement);

        Assert.Equal(Shape(fromEnvironment.Parameters), Shape(fromSwagger.Parameters));
        Assert.Equal(fromEnvironment.ActionType, fromSwagger.ActionType);
        Assert.Equal(fromEnvironment.Summary, fromSwagger.Summary);
        Assert.Equal("$ref:poster", Param(fromSwagger, "location").DynamicValues!.Parameters!["poster"]);
        Assert.Equal("GetUnifiedActionSchema", Param(fromSwagger, "body").DynamicSchema!.OperationId);
    }

    [Fact]
    public void StaticBody_IsFlattenedUnderTheBodyParameterName_WithNestedRefsResolved()
    {
        var operation = _reader.ReadOperation(TeamsLike, "shared_teams", "SendCard");

        Assert.Equal(["teamId", "card/title", "card/importance", "card/recipient/id", "card/recipient/kind"],
            operation.Parameters.Select(p => p.Name));
        Assert.True(Param(operation, "teamId").Required);
        Assert.True(Param(operation, "card/title").Required);
        Assert.False(Param(operation, "card/importance").Required);
        Assert.Equal(["low", "high"], Param(operation, "card/importance").AllowedValues);
        Assert.Equal("Recipient id", Param(operation, "card/recipient/id").Description);
        Assert.Equal(["body/id", "body/createdOn"], operation.Outputs!.Select(o => o.Name));
    }

    [Fact]
    public void Triggers_AreMarked_AndAWebhookTriggerReadsItsNotificationPayload()
    {
        var polling = _reader.ReadOperation(TeamsLike, "shared_teams", "OnNewChannelMessage");
        var webhook = _reader.ReadOperation(TeamsLike, "shared_teams", "WebhookMentionTrigger");

        Assert.True(polling.IsTrigger);
        Assert.Equal(OperationSchemaReader.OpenApiConnection, polling.ActionType);
        Assert.True(webhook.IsTrigger);
        Assert.Equal(OperationSchemaReader.OpenApiConnectionWebhook, webhook.ActionType);
        Assert.DoesNotContain(webhook.Parameters, p => p.Name == "callbackUrl");
        Assert.Equal(["body/messageId"], webhook.Outputs!.Select(o => o.Name));
    }

    [Fact]
    public void WebhookAction_TakesACallbackUrl_AndDeprecationIsRead()
    {
        var operation = _reader.ReadOperation(TeamsLike, "shared_teams", "PostCardAndWaitForResponse");

        Assert.False(operation.IsTrigger);
        Assert.Equal(OperationSchemaReader.OpenApiConnectionWebhook, operation.ActionType);
        Assert.True(operation.IsDeprecated);
        Assert.Empty(operation.Parameters);
    }

    private static IEnumerable<string> Shape(IEnumerable<OperationParameter> parameters)
        => parameters.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => JsonSerializer.Serialize(p));

    private static OperationParameter Param(OperationDetail operation, string name)
        => Assert.Single(operation.Parameters, p => p.Name == name);

    /// <summary>
    /// Shaped like the Teams connector's swagger: shared parameters, legacy
    /// dynamic-values arguments, a connectionId the platform fills in, and both
    /// kinds of trigger.
    /// </summary>
    private const string TeamsLike = """
        {
          "swagger": "2.0",
          "info": { "title": "Teams-like", "version": "1.0" },
          "parameters": {
            "poster": { "name": "poster", "in": "path", "required": true, "type": "string", "x-ms-summary": "Post as", "enum": [ "Flow bot", "User" ] },
            "requestId": { "name": "x-ms-client-request-id", "in": "header", "type": "string", "x-ms-summary": "Request id" }
          },
          "paths": {
            "/{connectionId}/beta/teams/conversation/message/poster/{poster}/location/{location}": {
              "parameters": [ { "$ref": "#/parameters/requestId" } ],
              "post": {
                "operationId": "PostMessageToConversation",
                "summary": "Post message in a chat or channel",
                "parameters": [
                  { "name": "connectionId", "in": "path", "required": true, "type": "string", "x-ms-visibility": "internal" },
                  { "$ref": "#/parameters/poster" },
                  { "name": "location", "in": "path", "required": true, "type": "string", "x-ms-summary": "Post in",
                    "x-ms-dynamic-values": { "operationId": "GetMessageLocations", "value-collection": "value", "value-path": "id", "value-title": "displayName",
                      "parameters": { "messageType": "ParentMessage", "poster": { "parameter": "poster" } } } },
                  { "name": "body", "in": "body", "required": true, "schema": { "$ref": "#/definitions/DynamicPostMessageRequest" } }
                ],
                "responses": { "200": { "description": "OK" } }
              }
            },
            "/{connectionId}/teams/{teamId}/cards": {
              "post": {
                "operationId": "SendCard",
                "parameters": [
                  { "name": "teamId", "in": "path", "required": true, "type": "string" },
                  { "name": "card", "in": "body", "required": true, "schema": { "$ref": "#/definitions/Card" } }
                ],
                "responses": { "201": { "description": "Created", "schema": { "allOf": [ { "$ref": "#/definitions/Entity" } ] } } }
              }
            },
            "/{connectionId}/trigger/channelmessages": {
              "get": {
                "operationId": "OnNewChannelMessage",
                "x-ms-trigger": "batch",
                "parameters": [ { "name": "teamId", "in": "query", "type": "string" } ],
                "responses": { "200": { "description": "OK", "schema": { "type": "object", "properties": { "value": { "type": "array", "items": { "type": "object" } } } } } }
              }
            },
            "/{connectionId}/subscriptions/mention": {
              "x-ms-notification-content": { "schema": { "type": "object", "properties": { "messageId": { "type": "string" } } } },
              "post": {
                "operationId": "WebhookMentionTrigger",
                "x-ms-trigger": "single",
                "parameters": [ { "name": "callbackUrl", "in": "query", "type": "string", "x-ms-notification-url": true, "x-ms-visibility": "internal" } ],
                "responses": { "201": { "description": "Subscribed" } }
              }
            },
            "/{connectionId}/cards/wait": {
              "post": {
                "operationId": "PostCardAndWaitForResponse",
                "deprecated": true,
                "parameters": [ { "name": "callbackUrl", "in": "body", "schema": { "type": "string" }, "x-ms-notification-url": true } ],
                "responses": { "200": { "description": "OK" } }
              }
            }
          },
          "definitions": {
            "DynamicPostMessageRequest": {
              "type": "object",
              "x-ms-dynamic-schema": { "operationId": "GetUnifiedActionSchema", "value-path": "schema",
                "parameters": { "actionType": "Message", "poster": { "parameter": "poster" }, "recipientType": { "parameter": "location" } } }
            },
            "Card": {
              "type": "object",
              "required": [ "title" ],
              "properties": {
                "title": { "type": "string" },
                "importance": { "type": "string", "enum": [ "low", "high" ] },
                "recipient": { "$ref": "#/definitions/Recipient" }
              }
            },
            "Recipient": {
              "type": "object",
              "properties": { "id": { "type": "string", "x-ms-summary": "Recipient id" }, "kind": { "type": "string" } }
            },
            "Entity": {
              "type": "object",
              "properties": { "id": { "type": "string" }, "createdOn": { "type": "string", "format": "date-time", "readOnly": true } }
            }
          }
        }
        """;
}
