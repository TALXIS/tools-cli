using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TALXIS.CLI.Core;
using TALXIS.CLI.Features.Data;
using Xunit;

namespace TALXIS.CLI.Tests.Data.DataModelConverter;

[Collection("TxcServicesSerial")]
public class DetailCommandTests
{
    private static async Task<(int Exit, string Stdout)> Run(DataModelConvertCliCommand command)
    {
        var stdout = new StringWriter();
        using (OutputWriter.RedirectTo(stdout))
        {
            var exit = await command.RunAsync();
            return (exit, stdout.ToString());
        }
    }

    private static DataModelConvertCliCommand Command(
        TestTree tree, string declarations, string detail = "full", string target = "dbml", string format = "json", string? app = "contoso_app") =>
        new()
        {
            InputPaths = [declarations],
            AppUniqueName = app,
            Detail = detail,
            TargetFormat = target,
            OutputDirectory = tree.Output,
            Format = format,
        };

    private static string[] Lines(string text) => text.Split(['\r', '\n'], System.StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public async Task AtFull_TheEnvelopeKeepsItsFields_AndAddsTheOutputFileAndDetail()
    {
        using var tree = new TestTree();
        var command = Command(tree, tree.OrderApp());
        command.ShowDropped = true;

        var (exit, stdout) = await Run(command);

        Assert.Equal(0, exit);
        using var json = JsonDocument.Parse(stdout);
        var root = json.RootElement;
        var output = Path.Combine(tree.Output, "solution.dbml");
        Assert.Equal(["status", "message"], root.EnumerateObject().Select(property => property.Name).Take(2));
        Assert.Equal("succeeded", root.GetProperty("status").GetString());
        Assert.Equal($"Output written to: {output}", root.GetProperty("message").GetString());
        Assert.Equal(output, root.GetProperty("outputFile").GetString());
        Assert.Equal("full", root.GetProperty("detail").GetString());
        Assert.False(root.TryGetProperty("columnsDropped", out _));
        Assert.False(root.TryGetProperty("droppedByReason", out _));
        Assert.False(root.TryGetProperty("droppedColumns", out _));
    }

    [Fact]
    public async Task AtMinimal_TheCountsPerReasonComeWithoutTheColumns()
    {
        using var tree = new TestTree();

        var (exit, stdout) = await Run(Command(tree, tree.OrderApp(), detail: "minimal"));

        Assert.Equal(0, exit);
        using var json = JsonDocument.Parse(stdout);
        var root = json.RootElement;
        Assert.Equal("succeeded", root.GetProperty("status").GetString());
        Assert.Equal("minimal", root.GetProperty("detail").GetString());
        Assert.Equal(3, root.GetProperty("columnsDropped").GetInt32());
        Assert.Equal(
            [("no-reference-found", 2), ("platform-plumbing", 1)],
            root.GetProperty("droppedByReason").EnumerateArray().Select(count => (count.GetProperty("reason").GetString(), count.GetProperty("count").GetInt32())));
        Assert.False(root.TryGetProperty("droppedColumns", out _));
    }

    [Fact]
    public async Task AtMinimal_TheColumnsAreListedWhenAsked_WithTheSpellingTheCountsUse()
    {
        using var tree = new TestTree();
        var command = Command(tree, tree.OrderApp(), detail: "minimal");
        command.ShowDropped = true;

        var (exit, stdout) = await Run(command);

        Assert.Equal(0, exit);
        using var json = JsonDocument.Parse(stdout);
        var root = json.RootElement;
        var listed = root.GetProperty("droppedColumns").EnumerateArray()
            .Select(column => (column.GetProperty("table").GetString(), column.GetProperty("column").GetString(), column.GetProperty("reason").GetString()))
            .ToList();
        Assert.Equal(
            [
                ("contoso_order", "contoso_kind", "no-reference-found"),
                ("contoso_order", "modifiedon", "no-reference-found"),
                ("contoso_order", "owningname", "platform-plumbing"),
            ],
            listed);
        var counted = root.GetProperty("droppedByReason").EnumerateArray().Select(count => count.GetProperty("reason").GetString()).Order();
        Assert.Equal(counted, listed.Select(column => column.Item3).Distinct().Order());
    }

    [Fact]
    public async Task InTextMode_TheCountsAreShown_AndTheColumnsOnlyWhenAsked()
    {
        using var tree = new TestTree();
        var declarations = tree.OrderApp();

        var (_, counts) = await Run(Command(tree, declarations, detail: "minimal", format: "text"));
        var asked = Command(tree, declarations, detail: "minimal", format: "text");
        asked.ShowDropped = true;
        var (_, listed) = await Run(asked);

        Assert.Equal(
            [$"Output written to: {Path.Combine(tree.Output, "solution.dbml")}", "  dropped 2 column(s): no-reference-found", "  dropped 1 column(s): platform-plumbing"],
            Lines(counts));
        Assert.Equal(
            [
                "    contoso_order.contoso_kind (no-reference-found)",
                "    contoso_order.modifiedon (no-reference-found)",
                "    contoso_order.owningname (platform-plumbing)",
            ],
            Lines(listed).Skip(3));
    }

    [Fact]
    public async Task AtFull_TextOutputIsTheOneLineItHasAlwaysBeen()
    {
        using var tree = new TestTree();

        var (exit, stdout) = await Run(Command(tree, tree.OrderApp(), format: "text"));

        Assert.Equal(0, exit);
        Assert.Equal([$"Output written to: {Path.Combine(tree.Output, "solution.dbml")}"], Lines(stdout));
    }

    [Fact]
    public async Task MinimalWithoutAnApp_IsAValidationError_BeforeAnythingIsCreated()
    {
        using var tree = new TestTree();
        var command = Command(tree, tree.OrderApp(), detail: "minimal", app: null);
        command.OutputDirectory = tree.Full("not-created");

        var (exit, stdout) = await Run(command);

        Assert.Equal(2, exit);
        Assert.Equal("", stdout);
        Assert.False(Directory.Exists(tree.Full("not-created")));
    }

    [Theory]
    [InlineData("sql")]
    [InlineData("plainsql")]
    [InlineData("edmx")]
    [InlineData("ribbon")]
    public async Task MinimalForATargetOtherThanDbml_IsAValidationError_BeforeAnythingIsCreated(string target)
    {
        using var tree = new TestTree();
        var command = Command(tree, tree.OrderApp(), detail: "minimal", target: target);
        command.OutputDirectory = tree.Full("not-created");

        var (exit, stdout) = await Run(command);

        Assert.Equal(2, exit);
        Assert.Equal("", stdout);
        Assert.False(Directory.Exists(tree.Full("not-created")));
    }
}
