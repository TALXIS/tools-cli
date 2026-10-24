using System.Linq;
using System.Xml.Linq;
using TALXIS.CLI.Features.Data.DataModelConverter;
using TALXIS.CLI.Features.Data.DataModelConverter.AppScope;
using Model = TALXIS.CLI.Features.Data.DataModelConverter.Model;
using Xunit;

namespace TALXIS.CLI.Tests.Data.DataModelConverter;

public class MinimalDetailRelationshipTests
{
    private static Model.ParsedModel Parse(DetailLevel detail, string[] inApp, string[] tables, params string[] relationships)
    {
        var module = new Model.Module { ModuleName = "test" };
        module.entities.AddRange(tables.Select(table => XElement.Parse(TestTree.Entity(table))));
        module.relationships.AddRange(relationships.Select(XElement.Parse));

        var scope = new ResolvedAppScope { UniqueName = "contoso_app", Detail = detail };
        scope.TableLogicalNames.UnionWith(inApp);
        return DataModelConverterService.ParseModules([module], scope);
    }

    private static bool HasTable(Model.ParsedModel model, string name) => model.tables.Any(table => table.LogicalName == name);

    [Theory]
    [InlineData("contoso_inapp", "contoso_outside", DetailLevel.Minimal, false)]
    [InlineData("contoso_outside", "contoso_inapp", DetailLevel.Minimal, false)]
    [InlineData("contoso_inapp", "contoso_alsoinapp", DetailLevel.Minimal, true)]
    [InlineData("contoso_inapp", "contoso_inapp", DetailLevel.Minimal, true)]
    [InlineData("contoso_inapp", "contoso_outside", DetailLevel.Full, true)]
    [InlineData("contoso_outside", "contoso_inapp", DetailLevel.Full, true)]
    public void AManyToMany_NeedsBothSidesInTheAppUnderMinimal_AndEitherAtFull(string first, string second, DetailLevel detail, bool kept)
    {
        var model = Parse(
            detail,
            ["contoso_inapp", "contoso_alsoinapp"],
            ["contoso_inapp", "contoso_alsoinapp", "contoso_outside"],
            TestTree.ManyToMany("contoso_link", first, second));

        Assert.Equal(kept, HasTable(model, "contoso_link"));
        Assert.Equal(kept, model.relationships.Count > 0);
    }

    [Fact]
    public void DroppingAnManyToMany_DoesNotRemoveAStubAnOrdinaryLookupStillNeeds()
    {
        var model = Parse(
            DetailLevel.Minimal,
            ["contoso_inapp"],
            ["contoso_inapp", "contoso_outside"],
            TestTree.ManyToMany("contoso_link", "contoso_inapp", "contoso_outside"),
            TestTree.OneToMany("contoso_inapp", "contoso_outsideid", "contoso_outside"));

        Assert.False(HasTable(model, "contoso_link"));
        Assert.True(HasTable(model, "contoso_outside"));
        Assert.Single(model.relationships);
    }

    [Fact]
    public void EveryTargetStillRendersFromAModelNarrowedToAnApp()
    {
        var model = Parse(
            DetailLevel.Minimal,
            ["contoso_inapp"],
            ["contoso_inapp", "contoso_declared", "contoso_outside"],
            TestTree.OneToMany("contoso_inapp", "contoso_declaredid", "contoso_declared"),
            TestTree.ManyToMany("contoso_link", "contoso_inapp", "contoso_outside"));

        Assert.Null(Record.Exception(() => DataModelConverterService.ConvertToDBML(model)));
        Assert.Null(Record.Exception(() => DataModelConverterService.ConvertToSQL(model)));
        Assert.Null(Record.Exception(() => DataModelConverterService.ConvertToEDSSQL(model)));
        Assert.Null(Record.Exception(() => DataModelConverterService.ConvertToEDMX(model)));
        Assert.Null(Record.Exception(() => DataModelConverterService.ConvertToRibbonDiff(model)));
    }
}
