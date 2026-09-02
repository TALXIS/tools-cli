using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using TALXIS.CLI.Features.Data.DataModelConverter;
using TALXIS.CLI.Features.Data.DataModelConverter.AppScope;
using TALXIS.CLI.Features.Data.DataModelConverter.Translators;
using Model = TALXIS.CLI.Features.Data.DataModelConverter.Model;
using Xunit;

namespace TALXIS.CLI.Tests.Data.DataModelConverter;

/// <summary>
/// The table side of a design view. An N:N belongs to an app's design only when both of its
/// tables do — admitting one on a single side dragged nine systemuser intersects and four
/// far-side stubs into one real app — and a stub for a table the inputs do declare must not
/// be coloured as missing from the solution, which was true of 13 of 14 stubs there.
/// </summary>
public class MinimalDetailTableTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "txc-detail-" + Path.GetRandomFileName());

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static XElement Entity(string logicalName, params string[] columns) =>
        XElement.Parse($"""
            <Entity>
              <Name LocalizedName="{logicalName}" OriginalName="{logicalName}">{logicalName}</Name>
              <EntityInfo><entity Name="{logicalName}"><attributes>
                <attribute PhysicalName="{logicalName}id"><Type>primarykey</Type></attribute>
                {string.Join("", columns.Select(c => $"""<attribute PhysicalName="{c}"><Type>lookup</Type><IsCustomField>1</IsCustomField></attribute>"""))}
              </attributes></entity></EntityInfo>
            </Entity>
            """);

    private static XElement ManyToMany(string name, string first, string second) =>
        XElement.Parse($"""
            <EntityRelationship Name="{name}">
              <EntityRelationshipType>ManyToMany</EntityRelationshipType>
              <FirstEntityName>{first}</FirstEntityName>
              <SecondEntityName>{second}</SecondEntityName>
              <IntersectEntityName>{name}</IntersectEntityName>
            </EntityRelationship>
            """);

    private static XElement OneToMany(string name, string referencing, string referenced, string attribute) =>
        XElement.Parse($"""
            <EntityRelationship Name="{name}">
              <EntityRelationshipType>OneToMany</EntityRelationshipType>
              <ReferencingEntityName>{referencing}</ReferencingEntityName>
              <ReferencedEntityName>{referenced}</ReferencedEntityName>
              <ReferencingAttributeName>{attribute}</ReferencingAttributeName>
            </EntityRelationship>
            """);

    private ResolvedAppScope ScopeFor(DetailLevel detail, params string[] tables)
    {
        var scope = new ResolvedAppScope { UniqueName = "contoso_app", Detail = detail };
        scope.SearchRoots.Add(_root);
        foreach (var table in tables) scope.TableLogicalNames.Add(table);
        return scope;
    }

    private static bool HasTable(Model.ParsedModel model, string name) =>
        model.tables.Any(t => string.Equals(t.LogicalName, name, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void AnNToNWithOnlyOneSideInTheApp_IsDroppedInDesign_AndKeptAtFullDetail()
    {
        // The one gate this change turns. Full detail admits the intersect on either side,
        // which is deliberate there; design asks whether the association is the app's own.
        Model.ParsedModel Convert(DetailLevel detail)
        {
            var module = new Model.Module { ModuleName = "m" };
            module.entities.Add(Entity("contoso_inapp"));
            module.entities.Add(Entity("contoso_stranger"));
            module.relationships.Add(ManyToMany("contoso_inapp_stranger", "contoso_inapp", "contoso_stranger"));
            return DataModelConverterService.ParseModules([module], ScopeFor(detail, "contoso_inapp"));
        }

        Assert.False(HasTable(Convert(DetailLevel.Minimal), "contoso_inapp_stranger"));
        Assert.True(HasTable(Convert(DetailLevel.Full), "contoso_inapp_stranger"));
    }

    [Fact]
    public void AnNToNWithBothSidesInTheApp_Survives()
    {
        // The rule must not cost an association the app genuinely owns.
        var module = new Model.Module { ModuleName = "m" };
        module.entities.Add(Entity("contoso_left"));
        module.entities.Add(Entity("contoso_right"));
        module.relationships.Add(ManyToMany("contoso_left_right", "contoso_left", "contoso_right"));

        var model = DataModelConverterService.ParseModules([module], ScopeFor(DetailLevel.Minimal, "contoso_left", "contoso_right"));

        Assert.True(HasTable(model, "contoso_left_right"));
    }

    [Fact]
    public void DroppingAnNToN_DoesNotRemoveAStubAnOrdinaryLookupStillNeeds()
    {
        // Suppression is per relationship, not "erase every table a dropped edge touched" --
        // Account and one contract table survived exactly this way in a real app.
        var module = new Model.Module { ModuleName = "m" };
        module.entities.Add(Entity("contoso_inapp", "contoso_strangerid"));
        module.entities.Add(Entity("contoso_stranger"));
        module.relationships.Add(ManyToMany("contoso_inapp_stranger", "contoso_inapp", "contoso_stranger"));
        module.relationships.Add(OneToMany("contoso_lookup", "contoso_inapp", "contoso_stranger", "contoso_strangerid"));

        var model = DataModelConverterService.ParseModules([module], ScopeFor(DetailLevel.Minimal, "contoso_inapp"));

        Assert.False(HasTable(model, "contoso_inapp_stranger"));
        Assert.True(HasTable(model, "contoso_stranger"));
    }

    [Fact]
    public void AStubForATableAnInputDeclares_IsMarkedAsOutsideTheApp_AndColouredDifferently()
    {
        // 13 of 14 stubs in a real app were declared as full entities in the same inputs, so
        // the red "not in the solution" was untrue for almost all of them.
        var module = new Model.Module { ModuleName = "m" };
        module.entities.Add(Entity("contoso_inapp", "contoso_declaredid"));
        module.entities.Add(Entity("contoso_declared"));
        module.relationships.Add(OneToMany("contoso_lookup", "contoso_inapp", "contoso_declared", "contoso_declaredid"));

        var model = DataModelConverterService.ParseModules([module], ScopeFor(DetailLevel.Minimal, "contoso_inapp"));
        var stub = model.tables.Single(t => t.LogicalName == "contoso_declared");

        Assert.Equal(Model.TableType.NotInApp, stub.Type);
        Assert.Contains("#7f8c8d", stub.ToDbDiagramNotation());
    }

    [Fact]
    public void AStubForATableNoInputDeclares_KeepsTheColourThatSaysSo()
    {
        // The platform's own tables really are absent from the inputs, and a reader needs to
        // keep being told that.
        var module = new Model.Module { ModuleName = "m" };
        module.entities.Add(Entity("contoso_inapp", "contoso_absentid"));
        module.relationships.Add(OneToMany("contoso_lookup", "contoso_inapp", "contoso_absent", "contoso_absentid"));

        var model = DataModelConverterService.ParseModules([module], ScopeFor(DetailLevel.Minimal, "contoso_inapp"));
        var stub = model.tables.Single(t => t.LogicalName == "contoso_absent");

        Assert.Equal(Model.TableType.NotInSolution, stub.Type);
        Assert.Contains("#c0392b", stub.ToDbDiagramNotation());
    }

    [Fact]
    public void WithoutAnAppScope_NoStubIsEverMarkedAsOutsideOne()
    {
        // "Outside the app" is only meaningful when an app was named; converting a whole
        // solution must keep saying what it says today.
        var module = new Model.Module { ModuleName = "m" };
        module.entities.Add(Entity("contoso_thing", "contoso_absentid"));
        module.relationships.Add(OneToMany("contoso_lookup", "contoso_thing", "contoso_absent", "contoso_absentid"));

        var model = DataModelConverterService.ParseModules([module], null);

        Assert.DoesNotContain(model.tables, t => t.Type == Model.TableType.NotInApp);
    }

    [Fact]
    public void EveryTargetStillRendersAfterTablesAndColumnsAreDropped()
    {
        // The crash class this change risks: the SQL and EDMX translators read a
        // relationship's endpoint tables and rows with no null check, so a table may never
        // go while an edge still points at it.
        var module = new Model.Module { ModuleName = "m" };
        module.entities.Add(Entity("contoso_inapp", "contoso_declaredid"));
        module.entities.Add(Entity("contoso_declared"));
        module.entities.Add(Entity("contoso_stranger"));
        module.relationships.Add(OneToMany("contoso_lookup", "contoso_inapp", "contoso_declared", "contoso_declaredid"));
        module.relationships.Add(ManyToMany("contoso_inapp_stranger", "contoso_inapp", "contoso_stranger"));

        var model = DataModelConverterService.ParseModules([module], ScopeFor(DetailLevel.Minimal, "contoso_inapp"));

        Assert.Null(Record.Exception(() => DataModelConverterService.ConvertToDBML(model)));
        Assert.Null(Record.Exception(() => DataModelConverterService.ConvertToSQL(model)));
        Assert.Null(Record.Exception(() => DataModelConverterService.ConvertToEDSSQL(model)));
        Assert.Null(Record.Exception(() => DataModelConverterService.ConvertToEDMX(model)));
        Assert.Null(Record.Exception(() => DataModelConverterService.ConvertToRibbonDiff(model)));
    }
}
