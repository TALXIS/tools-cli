using System.Text.Json;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Platform.PowerPlatform.Control.PowerAutomate;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Connector;

public class SwaggerOperationIndexerTests
{
    private const string Payload = """
        { "name": "shared_sharepointonline", "properties": { "displayName": "SharePoint", "swagger": { "paths": {
          "/items": {
            "get": { "operationId": "GetItems", "summary": "Get items" },
            "post": { "operationId": "PostItem", "summary": "Create item", "deprecated": true }
          },
          "/subscriptions": { "post": { "operationId": "CreateOnNewItemPokeSubscription", "x-ms-visibility": "internal" } },
          "/trigger/items": { "get": { "operationId": "GetOnNewItems", "summary": "When an item is created", "x-ms-trigger": "batch" } },
          "/hooks": {
            "x-ms-notification-content": { "schema": {} },
            "post": { "operationId": "OnNewFile", "summary": "When a file is created", "x-ms-trigger": "single" }
          }
        } } } }
        """;

    [Fact]
    public void Index_ClassifiesActionsTriggersAndWebhookTriggers()
    {
        var detail = Index(search: null);

        Assert.Equal(4, detail.OperationCount);
        Assert.Equal(ConnectorOperationKind.Action, Op(detail, "GetItems").Kind);
        Assert.Equal(ConnectorOperationKind.Trigger, Op(detail, "GetOnNewItems").Kind);
        Assert.Equal(ConnectorOperationKind.WebhookTrigger, Op(detail, "OnNewFile").Kind);
        Assert.True(Op(detail, "PostItem").IsDeprecated);
        Assert.Equal("/providers/Microsoft.PowerApps/apis/shared_sharepointonline", detail.ApiId);
    }

    [Fact]
    public void Index_LeavesOutInternalPlumbingOperations()
    {
        // Seen live on Outlook: Create...PokeSubscription calls are internal
        // helpers of webhook triggers that the designer never offers.
        Assert.DoesNotContain(Index(search: null).Operations, o => o.OperationId == "CreateOnNewItemPokeSubscription");
    }

    [Fact]
    public void Index_FiltersByOperationIdOrSummary()
    {
        var detail = Index(search: "created");

        Assert.Equal(4, detail.OperationCount);
        Assert.Equal(2, detail.MatchedOperations);
        Assert.Equal(["GetOnNewItems", "OnNewFile"], detail.Operations.Select(o => o.OperationId));
    }

    private static ConnectorDetail Index(string? search)
    {
        using var document = JsonDocument.Parse(Payload);
        return SwaggerOperationIndexer.Index(document.RootElement, "fallback", search);
    }

    private static ConnectorOperationSummary Op(ConnectorDetail detail, string id)
        => Assert.Single(detail.Operations, o => o.OperationId == id);
}
