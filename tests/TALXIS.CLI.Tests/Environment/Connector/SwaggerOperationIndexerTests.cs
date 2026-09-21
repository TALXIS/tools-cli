using System.Text.Json;
using TALXIS.CLI.Platform.PowerPlatform.Control.PowerAutomate;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Connector;

public class SwaggerOperationIndexerTests
{
    /// <summary>
    /// A connector payload shaped like the real one: a trigger, a normal action
    /// whose body is a <c>$ref</c>, and a deprecated action.
    /// </summary>
    private const string ConnectorPayload = """
        {
          "name": "shared_teams",
          "properties": {
            "displayName": "Microsoft Teams",
            "swagger": {
              "paths": {
                "/v3/beta/me/conversations": {
                  "get": {
                    "operationId": "OnNewChannelMessage",
                    "summary": "When a new channel message is added",
                    "x-ms-trigger": "batched",
                    "parameters": [ { "name": "groupId", "in": "query", "type": "string" } ]
                  }
                },
                "/v3/beta/teams/conversation": {
                  "post": {
                    "operationId": "PostMessageToConversation",
                    "summary": "Post a message in a chat or channel",
                    "parameters": [
                      { "name": "poster", "in": "path", "type": "string" },
                      { "name": "body", "in": "body", "schema": { "properties": { "recipient": {}, "messageBody": {} } } }
                    ]
                  }
                },
                "/beta/teams/channel": {
                  "post": {
                    "operationId": "PostMessageToChannel",
                    "summary": "Post a message (deprecated)",
                    "deprecated": true,
                    "parameters": []
                  }
                }
              }
            }
          }
        }
        """;

    [Fact]
    public void Index_ReturnsEveryOperationWithItsMethodAndPath()
    {
        var connector = Index();

        Assert.Equal("shared_teams", connector.Name);
        Assert.Equal("Microsoft Teams", connector.DisplayName);
        Assert.Equal("/providers/Microsoft.PowerApps/apis/shared_teams", connector.ApiId);
        Assert.Equal(3, connector.OperationCount);

        var post = Assert.Single(connector.Operations, o => o.OperationId == "PostMessageToConversation");
        Assert.Equal("POST", post.Method);
        Assert.Equal("/v3/beta/teams/conversation", post.Path);
    }

    [Fact]
    public void Index_CountsBodyPropertiesRatherThanTheBodyWrapper()
    {
        // One path parameter plus two expanded body properties: what an author
        // actually fills in, not the single "body" parameter.
        var post = Assert.Single(Index().Operations, o => o.OperationId == "PostMessageToConversation");

        Assert.Equal(3, post.ParamCount);
    }

    [Fact]
    public void Index_FlagsTriggersAndDeprecatedOperations()
    {
        var connector = Index();

        Assert.True(Assert.Single(connector.Operations, o => o.OperationId == "OnNewChannelMessage").IsTrigger);
        Assert.True(Assert.Single(connector.Operations, o => o.OperationId == "PostMessageToChannel").IsDeprecated);
        Assert.False(Assert.Single(connector.Operations, o => o.OperationId == "PostMessageToConversation").IsDeprecated);
    }

    [Fact]
    public void Index_WithQuery_FiltersAndReportsTheMatchCount()
    {
        // Matches two operation IDs plus one whose summary mentions a channel.
        var connector = Index("channel");

        Assert.Equal(3, connector.OperationCount);
        Assert.Equal(3, connector.MatchedOperations);
        Assert.All(connector.Operations, o =>
            Assert.Contains("channel", o.OperationId + o.Summary, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Index_WithQuery_ExcludesOperationsThatDoNotMatch()
    {
        var connector = Index("conversation");

        Assert.Equal(3, connector.OperationCount);
        Assert.Equal("PostMessageToConversation", Assert.Single(connector.Operations).OperationId);
    }

    [Fact]
    public void Index_WithoutQuery_DoesNotReportAMatchCount()
        => Assert.Null(Index().MatchedOperations);

    [Fact]
    public void Index_ToleratesAConnectorWithNoSwagger()
    {
        using var document = JsonDocument.Parse("""{ "name": "shared_bare", "properties": {} }""");
        var connector = SwaggerOperationIndexer.Index(document.RootElement, "shared_bare", null);

        Assert.Equal(0, connector.OperationCount);
        Assert.Empty(connector.Operations);
    }

    [Fact]
    public void Index_IgnoresNonMethodKeysUnderAPath()
    {
        // Swagger allows path-level "parameters" alongside the HTTP methods; it
        // must not be mistaken for an operation.
        using var document = JsonDocument.Parse("""
            {
              "name": "shared_x",
              "properties": { "swagger": { "paths": { "/a": {
                "parameters": { "operationId": "NotAnOperation" },
                "get": { "operationId": "Real" }
              } } } }
            }
            """);

        var connector = SwaggerOperationIndexer.Index(document.RootElement, "shared_x", null);

        Assert.Equal("Real", Assert.Single(connector.Operations).OperationId);
    }

    private static Core.Platforms.PowerPlatform.ConnectorDetail Index(string? query = null)
    {
        using var document = JsonDocument.Parse(ConnectorPayload);
        return SwaggerOperationIndexer.Index(document.RootElement, "shared_teams", query);
    }
}
