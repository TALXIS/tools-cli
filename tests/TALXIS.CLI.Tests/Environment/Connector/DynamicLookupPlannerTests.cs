using System.Text.Json;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Platform.PowerPlatform.Control.PowerAutomate;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Connector;

public class DynamicLookupPlannerTests
{
    private const string Runtime = "https://env.common.europe002.azure-apihub.net/apim/sharepointonline";

    // SharePoint-shaped: the lookup's parameters sit on the path item, and the
    // site URL in the path is encoded twice.
    private const string Operation = """
        { "properties": { "inputsDefinition": { "properties": {
          "dataset": { "type": "string" },
          "table": { "type": "string", "x-ms-dynamic-list": { "operationId": "GetTables",
            "parameters": { "dataset": { "parameterReference": "dataset" } } } }
        } } } }
        """;

    [Fact]
    public void PathItemParameters_AreFilledLikeTheOperationsOwn()
    {
        var planned = PlanTable("""
            "/{connectionId}/datasets/{dataset}/tables": {
              "parameters": [ { "name": "connectionId", "in": "path", "required": true },
                              { "name": "dataset", "in": "path", "required": true } ],
              "get": { "operationId": "GetTables" } }
            """, ("dataset", "https://contoso.sharepoint.com/sites/x"));

        Assert.Equal("planned", planned.Lookup.Status);
        Assert.Equal(
            Runtime + "/conn/datasets/https%253A%252F%252Fcontoso.sharepoint.com%252Fsites%252Fx/tables",
            planned.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public void DoubleEncoding_IsOnlyAppliedWhenDeclared()
    {
        var planned = PlanTable("""
            "/{connectionId}/datasets/{dataset}/tables": { "get": { "operationId": "GetTables",
              "parameters": [ { "name": "connectionId", "in": "path" }, { "name": "dataset", "in": "path" } ] } }
            """, ("dataset", "a/b"), doubleEncoding: false);

        Assert.EndsWith("/datasets/a%2Fb/tables", planned.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public void APlaceholderNothingDeclares_SkipsTheLookupInsteadOfSendingItLiterally()
    {
        var planned = PlanTable("""
            "/{connectionId}/datasets/{dataset}/tables": { "get": { "operationId": "GetTables",
              "parameters": [ { "name": "connectionId", "in": "path" } ] } }
            """, ("dataset", "x"));

        Assert.Equal("skipped", planned.Lookup.Status);
        Assert.Contains("'dataset'", planned.Lookup.Reason);
        Assert.Null(planned.RequestUri);
    }

    private static DynamicLookupPlanner.PlannedLookup PlanTable(
        string path, (string Name, string Value) value, bool doubleEncoding = true)
    {
        if (doubleEncoding)
            path = path.Replace("""{ "name": "dataset", "in": "path", "required": true }""",
                """{ "name": "dataset", "in": "path", "required": true, "x-ms-url-encoding": "double" }""", StringComparison.Ordinal);

        using var operation = JsonDocument.Parse(Operation);
        using var connector = JsonDocument.Parse(
            $$"""{ "properties": { "primaryRuntimeUrl": "{{Runtime}}", "swagger": { "paths": { {{path}} } } } }""");
        var detail = OperationSchemaReader.Read("shared_sharepointonline", "GetItems", operation.RootElement);

        return Assert.Single(
            DynamicLookupPlanner.Plan(detail, connector.RootElement, "conn", new Dictionary<string, string> { [value.Name] = value.Value }),
            p => p.Parameter.Name == "table");
    }
}
