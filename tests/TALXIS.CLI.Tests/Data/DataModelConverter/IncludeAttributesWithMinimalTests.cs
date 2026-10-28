using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using TALXIS.CLI.Core;
using TALXIS.CLI.Features.Data;
using TALXIS.CLI.Features.Data.DataModelConverter;
using TALXIS.CLI.Features.Data.DataModelConverter.AppScope;
using Model = TALXIS.CLI.Features.Data.DataModelConverter.Model;
using Xunit;

namespace TALXIS.CLI.Tests.Data.DataModelConverter;

[Collection("TxcServicesSerial")]
public class IncludeAttributesWithMinimalTests
{
    private static Model.ParsedModel ParseMinimal(TestTree tree, string[] include, ResolvedAppScope? existing, params string[] attributes)
    {
        tree.Write("Model/Declarations/Entities/contoso_order/FormXml/main/form.xml", """<form><control datafieldname="contoso_shown" /></form>""");
        var module = new Model.Module { ModuleName = "test", CustomizationPrefix = "contoso" };
        module.entities.Add(XElement.Parse(TestTree.Entity("contoso_order", attributes)));

        var scope = existing ?? new ResolvedAppScope { UniqueName = "contoso_app" };
        scope.Detail = DetailLevel.Minimal;
        scope.IncludeAttributes = include;
        scope.SearchRoots.Add(tree.Root);
        scope.TableLogicalNames.Add("contoso_order");
        return DataModelConverterService.ParseModules([module], scope);
    }

    private static bool Has(Model.ParsedModel model, string column) =>
        model.tables.Single().Rows.Any(row => row.Name == column);

    private static async Task<(int Exit, string Stdout)> Run(DataModelConvertCliCommand command)
    {
        var stdout = new StringWriter();
        using (OutputWriter.RedirectTo(stdout))
        {
            var exit = await command.RunAsync();
            return (exit, stdout.ToString());
        }
    }

    [Fact]
    public void AColumnTheListNames_IsKeptWhereMinimalWouldDropIt()
    {
        using var tree = new TestTree();

        var model = ParseMinimal(tree, ["contoso_listed"], null, TestTree.Attr("contoso_listed", "int"), TestTree.Attr("contoso_unlisted", "int"));

        Assert.True(Has(model, "contoso_listed"));
        Assert.False(Has(model, "contoso_unlisted"));
    }

    [Fact]
    public void TheListAddsToTheColumnsTheAppRefersTo_RatherThanReplacingThem()
    {
        using var tree = new TestTree();

        var model = ParseMinimal(tree, ["contoso_listed"], null,
            TestTree.Attr("contoso_shown", "int"), TestTree.Attr("contoso_listed", "int"), TestTree.Attr("contoso_other", "int"));

        Assert.True(Has(model, "contoso_shown"));
        Assert.True(Has(model, "contoso_listed"));
        Assert.False(Has(model, "contoso_other"));
    }

    [Fact]
    public void AWildcardKeepsEveryColumnItMatches_AndTheRestStillNarrow()
    {
        using var tree = new TestTree();

        var model = ParseMinimal(tree, ["contoso_x*"], null,
            TestTree.Attr("contoso_x1", "int"), TestTree.Attr("contoso_x2", "int"), TestTree.Attr("contoso_y", "int"));

        Assert.True(Has(model, "contoso_x1"));
        Assert.True(Has(model, "contoso_x2"));
        Assert.False(Has(model, "contoso_y"));
    }

    [Fact]
    public void TheListOverridesPlatformPlumbing()
    {
        using var tree = new TestTree();

        var model = ParseMinimal(tree, ["owningname", "stageid"], null,
            TestTree.Attr("owningname", "nvarchar", isLogical: true), TestTree.Attr("stageid", "uniqueidentifier"));

        Assert.True(Has(model, "owningname"));
        Assert.True(Has(model, "stageid"));
    }

    [Fact]
    public void AColumnTheListKeeps_IsNotReportedAsDropped()
    {
        using var tree = new TestTree();
        var scope = new ResolvedAppScope { UniqueName = "contoso_app" };

        ParseMinimal(tree, ["contoso_listed"], scope, TestTree.Attr("contoso_listed", "int"), TestTree.Attr("contoso_unlisted", "int"));

        Assert.Equal([new DroppedColumn("contoso_order", "contoso_unlisted", DropReason.NoReferenceFound)], scope.DroppedColumns);
    }

    [Theory]
    [InlineData("contoso_*", "contoso_name", true)]
    [InlineData("contoso_*", "ownerid", false)]
    [InlineData("owner?d", "ownerid", true)]
    [InlineData("OWNERID", "ownerid", true)]
    [InlineData(" ownerid ", "ownerid", true)]
    public void TheMatcher_UnderstandsWildcardsCaseAndPadding(string pattern, string name, bool expected)
    {
        Assert.Equal(expected, AttributeFilter.Matcher([pattern])(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void TheMatcher_MatchesNothing_WithoutAUsablePattern(string? pattern)
    {
        Assert.False(AttributeFilter.Matcher(pattern == null ? null : new[] { pattern })("ownerid"));
    }

    [Fact]
    public void ConvertModel_AtMinimal_KeepsTheListedColumnsAsWell()
    {
        using var tree = new TestTree();
        var model = tree.OrderApp();
        var output = Path.Combine(tree.Output, "solution.dbml");

        var dropped = DataModelConverterService.ConvertModel(
            [model], "dbml", output, "contoso_app", includeAttributes: ["modifiedon", "contoso_kind"], detail: DetailLevel.Minimal);

        var dbml = File.ReadAllText(output);
        Assert.Contains("  contoso_total ", dbml);
        Assert.Contains("  modifiedon ", dbml);
        Assert.Contains("  contoso_kind ", dbml);
        Assert.Contains("Enum contoso_kind ", dbml);
        Assert.Equal([new DroppedColumn("contoso_order", "owningname", DropReason.PlatformPlumbing)], dropped);
    }

    [Fact]
    public void ConvertModel_AtFull_KeepsOnlyTheListedColumns()
    {
        using var tree = new TestTree();
        var model = tree.OrderApp();
        var output = Path.Combine(tree.Output, "solution.dbml");

        var dropped = DataModelConverterService.ConvertModel([model], "dbml", output, "contoso_app", includeAttributes: ["contoso_total"]);

        var dbml = File.ReadAllText(output);
        Assert.Contains("  contoso_total ", dbml);
        Assert.Contains("  contoso_orderid ", dbml);
        Assert.DoesNotContain("modifiedon", dbml);
        Assert.DoesNotContain("contoso_note", dbml);
        Assert.Empty(dropped);
    }

    [Fact]
    public void ConvertModel_AtFull_DropsTheOptionSetsOfTheColumnsTheListLeftOut()
    {
        using var tree = new TestTree();
        var model = tree.OrderApp();
        var output = Path.Combine(tree.Output, "solution.dbml");

        DataModelConverterService.ConvertModel([model], "dbml", output, "contoso_app", includeAttributes: ["contoso_total"]);

        Assert.DoesNotContain("contoso_kind", File.ReadAllText(output));
    }

    [Fact]
    public async Task TheCommand_AtMinimal_KeepsTheListedColumns()
    {
        using var tree = new TestTree();
        var command = new DataModelConvertCliCommand
        {
            InputPaths = [tree.OrderApp()],
            AppUniqueName = "contoso_app",
            Detail = "minimal",
            IncludeAttributes = "modifiedon, contoso_kind",
            TargetFormat = "dbml",
            OutputDirectory = tree.Output,
            Format = "json",
        };

        var (exit, stdout) = await Run(command);

        Assert.Equal(0, exit);
        using var json = JsonDocument.Parse(stdout);
        Assert.Equal(1, json.RootElement.GetProperty("columnsDropped").GetInt32());
        Assert.Contains("  modifiedon ", tree.Dbml());
    }

    [Fact]
    public async Task TheCommand_AtFull_KeepsOnlyTheListedColumns()
    {
        using var tree = new TestTree();
        var command = new DataModelConvertCliCommand
        {
            InputPaths = [tree.OrderApp()],
            AppUniqueName = "contoso_app",
            IncludeAttributes = "contoso_total",
            TargetFormat = "dbml",
            OutputDirectory = tree.Output,
            Format = "json",
        };

        var (exit, _) = await Run(command);

        Assert.Equal(0, exit);
        var dbml = tree.Dbml();
        Assert.Contains("  contoso_total ", dbml);
        Assert.DoesNotContain("modifiedon", dbml);
    }
}
