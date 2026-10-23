using System.Text.Json;
using TALXIS.CLI.Platform.PowerPlatform.Control.PowerApps;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Providers.PowerPlatform;

public sealed class ConnectorSwaggerParserTests
{
    private const string Fixture = """
    {
      "swagger": "2.0",
      "paths": {
        "/items": {
          "post": {
            "operationId": "CreateItem",
            "summary": "Create an item",
            "parameters": [
              { "name": "folder", "in": "query", "type": "string", "required": true, "x-ms-summary": "Folder", "x-ms-dynamic-values": { "operationId": "ListFolders" } },
              { "name": "secret", "in": "header", "type": "string", "x-ms-visibility": "internal" },
              { "name": "item", "in": "body", "required": true, "schema": { "$ref": "#/definitions/Item" } }
            ]
          }
        },
        "/trigger-poll": {
          "get": { "operationId": "WhenItemChanges", "summary": "Poll trigger", "x-ms-trigger": "batch" }
        },
        "/trigger-hook": {
          "x-ms-notification-content": { "schema": { "type": "object" } },
          "post": { "operationId": "WhenHookFires", "summary": "Webhook trigger", "x-ms-trigger": "single", "x-ms-visibility": "important" }
        }
      },
      "definitions": {
        "Item": {
          "type": "object",
          "required": ["subject"],
          "properties": {
            "subject": { "type": "string", "x-ms-summary": "Subject" },
            "priority": { "type": "integer", "format": "int32", "enum": [1, 2] },
            "status": { "type": "string", "enum": ["Active", "Closed"] },
            "internalOnly": { "type": "string", "x-ms-visibility": "internal" },
            "createdOn": { "type": "string", "readOnly": true },
            "details": {
              "type": "object",
              "required": ["note"],
              "properties": { "note": { "type": "string" } }
            }
          }
        }
      }
    }
    """;

    private static JsonElement ParseFixture()
    {
        using var document = JsonDocument.Parse(Fixture);
        return document.RootElement.Clone();
    }

    [Fact]
    public void ListOperations_ClassifiesActionTriggerAndWebhookTrigger()
    {
        var operations = ConnectorSwaggerParser.ListOperations(ParseFixture());

        Assert.Equal(3, operations.Count);
        Assert.Equal("action", operations.Single(o => o.OperationId == "CreateItem").Kind);
        Assert.Equal("trigger", operations.Single(o => o.OperationId == "WhenItemChanges").Kind);
        Assert.Equal("webhook-trigger", operations.Single(o => o.OperationId == "WhenHookFires").Kind);
        Assert.Equal("important", operations.Single(o => o.OperationId == "WhenHookFires").Visibility);
    }

    [Fact]
    public void GetOperation_FlattensBodySchemaIntoSlashJoinedLeaves()
    {
        var detail = ConnectorSwaggerParser.GetOperation(
            ParseFixture(), "shared_test", "/providers/Microsoft.PowerApps/apis/shared_test", "CreateItem");

        Assert.NotNull(detail);
        Assert.Equal("POST", detail.HttpMethod);
        Assert.Equal("/items", detail.Path);
        Assert.Equal("/providers/Microsoft.PowerApps/apis/shared_test", detail.ApiId);

        var names = detail.Parameters.Select(p => p.Name).ToList();
        Assert.Contains("folder", names);
        Assert.Contains("item/subject", names);
        Assert.Contains("item/priority", names);
        Assert.Contains("item/status", names);
        Assert.Contains("item/details/note", names);
        Assert.DoesNotContain("secret", names);
        Assert.DoesNotContain("item/internalOnly", names);
        Assert.DoesNotContain("item/createdOn", names);
    }

    [Fact]
    public void GetOperation_PropagatesRequiredAndDynamicAndEnums()
    {
        var detail = ConnectorSwaggerParser.GetOperation(
            ParseFixture(), "shared_test", "/api", "CreateItem")!;

        var folder = detail.Parameters.Single(p => p.Name == "folder");
        Assert.True(folder.Required);
        Assert.True(folder.IsDynamic);
        Assert.Equal("query", folder.In);

        var subject = detail.Parameters.Single(p => p.Name == "item/subject");
        Assert.True(subject.Required);
        Assert.Equal("body", subject.In);

        // details is not listed in Item.required, so its children are optional.
        var note = detail.Parameters.Single(p => p.Name == "item/details/note");
        Assert.False(note.Required);

        var priority = detail.Parameters.Single(p => p.Name == "item/priority");
        Assert.Equal("integer (int32)", priority.Type);
        Assert.Equal(["1", "2"], priority.EnumValues);

        var status = detail.Parameters.Single(p => p.Name == "item/status");
        Assert.Equal(["Active", "Closed"], status.EnumValues);
    }

    [Fact]
    public void GetOperation_ReturnsNull_ForUnknownOperation()
    {
        var detail = ConnectorSwaggerParser.GetOperation(ParseFixture(), "shared_test", "/api", "DoesNotExist");
        Assert.Null(detail);
    }

    [Fact]
    public void GetOperation_MatchesOperationIdCaseInsensitively()
    {
        var detail = ConnectorSwaggerParser.GetOperation(ParseFixture(), "shared_test", "/api", "createitem");
        Assert.NotNull(detail);
        Assert.Equal("CreateItem", detail.OperationId);
    }

    [Fact]
    public void GetOperation_DeduplicatesPathItemParameters_OperationLevelWins()
    {
        const string fixture = """
        {
          "swagger": "2.0",
          "paths": {
            "/items/{id}": {
              "parameters": [
                { "name": "id", "in": "path", "type": "string", "required": true, "description": "path-level" }
              ],
              "get": {
                "operationId": "GetItem",
                "parameters": [
                  { "name": "id", "in": "path", "type": "string", "required": true, "description": "operation-level" }
                ]
              }
            }
          }
        }
        """;

        using var document = JsonDocument.Parse(fixture);
        var detail = ConnectorSwaggerParser.GetOperation(document.RootElement, "shared_test", "/api", "GetItem")!;

        var id = Assert.Single(detail.Parameters);
        Assert.Equal("operation-level", id.Description);
    }

    [Fact]
    public void GetOperation_EmitsBodyMarker_WhenFlattenYieldsNothing_EvenWithOtherParameters()
    {
        const string fixture = """
        {
          "swagger": "2.0",
          "paths": {
            "/items": {
              "post": {
                "operationId": "CreateItem",
                "parameters": [
                  { "name": "q", "in": "query", "type": "string" },
                  { "name": "item", "in": "body", "required": true, "schema": { "type": "object", "properties": { "hidden": { "type": "string", "x-ms-visibility": "internal" } } } }
                ]
              }
            }
          }
        }
        """;

        using var document = JsonDocument.Parse(fixture);
        var detail = ConnectorSwaggerParser.GetOperation(document.RootElement, "shared_test", "/api", "CreateItem")!;

        Assert.Contains(detail.Parameters, p => p.Name == "q");
        Assert.Contains(detail.Parameters, p => p.Name == "item" && p.In == "body");
    }

    [Fact]
    public void GetOperation_MarksNestedDynamicSchemaProperty_AsDynamic()
    {
        const string fixture = """
        {
          "swagger": "2.0",
          "paths": {
            "/rows": {
              "post": {
                "operationId": "CreateRow",
                "parameters": [
                  { "name": "item", "in": "body", "required": true, "schema": { "type": "object", "properties": { "row": { "type": "object", "x-ms-dynamic-schema": { "operationId": "GetSchema" } } } } }
                ]
              }
            }
          }
        }
        """;

        using var document = JsonDocument.Parse(fixture);
        var detail = ConnectorSwaggerParser.GetOperation(document.RootElement, "shared_test", "/api", "CreateRow")!;

        var row = detail.Parameters.Single(p => p.Name == "item/row");
        Assert.Equal("dynamic", row.Type);
        Assert.True(row.IsDynamic);
    }

    [Fact]
    public void GetOperation_MergesAllOfBranches()
    {
        const string fixture = """
        {
          "swagger": "2.0",
          "paths": {
            "/items": {
              "post": {
                "operationId": "CreateItem",
                "parameters": [
                  { "name": "item", "in": "body", "required": true, "schema": { "$ref": "#/definitions/Derived" } }
                ]
              }
            }
          },
          "definitions": {
            "Base": { "type": "object", "properties": { "id": { "type": "string" } } },
            "Derived": { "allOf": [ { "$ref": "#/definitions/Base" } ], "properties": { "name": { "type": "string" } } }
          }
        }
        """;

        using var document = JsonDocument.Parse(fixture);
        var detail = ConnectorSwaggerParser.GetOperation(document.RootElement, "shared_test", "/api", "CreateItem")!;

        var names = detail.Parameters.Select(p => p.Name).ToList();
        Assert.Contains("item/id", names);
        Assert.Contains("item/name", names);
    }

    [Fact]
    public void GetOperation_MarksDepthCappedObjects_AsTruncated()
    {
        const string fixture = """
        {
          "swagger": "2.0",
          "paths": {
            "/deep": {
              "post": {
                "operationId": "Deep",
                "parameters": [
                  { "name": "item", "in": "body", "schema": { "type": "object", "properties": { "l1": { "type": "object", "properties": { "l2": { "type": "object", "properties": { "l3": { "type": "object", "properties": { "l4": { "type": "object", "properties": { "l5": { "type": "object", "properties": { "leaf": { "type": "string" } } } } } } } } } } } } } }
                ]
              }
            }
          }
        }
        """;

        using var document = JsonDocument.Parse(fixture);
        var detail = ConnectorSwaggerParser.GetOperation(document.RootElement, "shared_test", "/api", "Deep")!;

        var capped = detail.Parameters.Single();
        Assert.Equal("object (truncated)", capped.Type);
    }

    [Fact]
    public void GetOperation_TreatsDynamicProperties_AsDynamicSchema()
    {
        const string fixture = """
        {
          "swagger": "2.0",
          "paths": {
            "/rows": {
              "post": {
                "operationId": "CreateRow",
                "parameters": [
                  { "name": "item", "in": "body", "required": true, "schema": { "type": "object", "x-ms-dynamic-properties": { "operationId": "GetSchema" } } }
                ]
              }
            }
          }
        }
        """;

        using var document = JsonDocument.Parse(fixture);
        var detail = ConnectorSwaggerParser.GetOperation(document.RootElement, "shared_test", "/api", "CreateRow")!;

        var parameter = Assert.Single(detail.Parameters);
        Assert.Equal("dynamic", parameter.Type);
        Assert.True(parameter.IsDynamic);
    }

    [Fact]
    public void GetOperation_EmitsSingleDynamicParameter_ForDynamicSchema()
    {
        const string dynamicFixture = """
        {
          "swagger": "2.0",
          "paths": {
            "/rows": {
              "post": {
                "operationId": "CreateRow",
                "parameters": [
                  { "name": "item", "in": "body", "required": true, "schema": { "type": "object", "x-ms-dynamic-schema": { "operationId": "GetSchema" } } }
                ]
              }
            }
          }
        }
        """;

        using var document = JsonDocument.Parse(dynamicFixture);
        var detail = ConnectorSwaggerParser.GetOperation(document.RootElement, "shared_test", "/api", "CreateRow")!;

        var parameter = Assert.Single(detail.Parameters);
        Assert.Equal("item", parameter.Name);
        Assert.Equal("dynamic", parameter.Type);
        Assert.True(parameter.IsDynamic);
        Assert.True(parameter.Required);
    }

    [Fact]
    public void GetOperation_ExposesResolverOperations_ForTeamsStyleDynamicParameters()
    {
        const string fixture = """
        {
          "swagger": "2.0",
          "paths": {
            "/beta/teams/conversation/message/poster/{poster}/location/{location}": {
              "post": {
                "operationId": "PostMessageToConversation",
                "parameters": [
                  { "name": "poster", "in": "path", "type": "string", "required": true, "enum": ["Flow bot", "User"] },
                  { "name": "location", "in": "path", "type": "string", "required": true,
                    "x-ms-dynamic-list": { "operationId": "GetMessageLocations", "parameters": { "messageType": { "value": "ParentMessage" }, "poster": { "parameterReference": "poster" } } } },
                  { "name": "body", "in": "body", "required": true,
                    "schema": { "type": "object", "x-ms-dynamic-properties": { "operationId": "GetUnifiedActionSchema", "parameters": { "actionType": { "value": "Message" }, "poster": { "parameterReference": "poster" }, "recipientType": { "parameterReference": "location" } } } } }
                ]
              }
            }
          }
        }
        """;

        using var document = JsonDocument.Parse(fixture);
        var detail = ConnectorSwaggerParser.GetOperation(document.RootElement, "shared_teams", "/api", "PostMessageToConversation")!;

        Assert.Null(detail.Parameters.Single(p => p.Name == "poster").DynamicSource);

        var location = detail.Parameters.Single(p => p.Name == "location").DynamicSource!;
        Assert.Equal("values", location.Kind);
        Assert.Equal("GetMessageLocations", location.OperationId);
        Assert.Equal("ParentMessage", location.Arguments["messageType"]);
        Assert.Equal("$ref:poster", location.Arguments["poster"]);

        var body = detail.Parameters.Single(p => p.Name == "body").DynamicSource!;
        Assert.Equal("schema", body.Kind);
        Assert.Equal("GetUnifiedActionSchema", body.OperationId);
        Assert.Equal(["actionType", "poster", "recipientType"], body.Arguments.Keys);
        Assert.Equal("$ref:location", body.Arguments["recipientType"]);
    }

    [Fact]
    public void GetOperation_ReadsLegacyDynamicValuesAndTreeResolvers()
    {
        const string fixture = """
        {
          "swagger": "2.0",
          "paths": {
            "/files": {
              "post": {
                "operationId": "CreateFile",
                "parameters": [
                  { "name": "dataset", "in": "query", "type": "string",
                    "x-ms-dynamic-values": { "operationId": "GetDatasets", "parameters": { "top": 10, "site": { "parameter": "siteUrl" } } } },
                  { "name": "folderPath", "in": "query", "type": "string",
                    "x-ms-dynamic-tree": { "settings": { "canSelectParentNodes": true }, "open": { "operationId": "ListRootFolder", "parameters": { "dataset": { "parameterReference": "dataset" } } } } }
                ]
              }
            }
          }
        }
        """;

        using var document = JsonDocument.Parse(fixture);
        var detail = ConnectorSwaggerParser.GetOperation(document.RootElement, "shared_test", "/api", "CreateFile")!;

        var dataset = detail.Parameters.Single(p => p.Name == "dataset").DynamicSource!;
        Assert.Equal("GetDatasets", dataset.OperationId);
        Assert.Equal("10", dataset.Arguments["top"]);
        Assert.Equal("$ref:siteUrl", dataset.Arguments["site"]);

        var folder = detail.Parameters.Single(p => p.Name == "folderPath").DynamicSource!;
        Assert.Equal("ListRootFolder", folder.OperationId);
        Assert.Equal("$ref:dataset", folder.Arguments["dataset"]);
    }
}
