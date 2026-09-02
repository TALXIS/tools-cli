using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using TALXIS.CLI.Features.Data.DataModelConverter;
using TALXIS.CLI.Features.Data.DataModelConverter.AppScope;
using Model = TALXIS.CLI.Features.Data.DataModelConverter.Model;
using Xunit;

namespace TALXIS.CLI.Tests.Data.DataModelConverter;

/// <summary>
/// A reference belongs to the table whose artefact made it. Matching on the name alone keeps
/// a column on every table declaring that name, which is why one view showing createdon kept
/// it on nineteen tables of a real app.
/// </summary>
public class PerTableColumnScopeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "txc-pertable-" + Path.GetRandomFileName());

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private void WriteArtefact(string entity, string folder, string fileName, string contents)
    {
        var dir = Path.Combine(_root, "module", "Declarations", "Entities", entity, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName), contents);
    }

    private void WriteSitemap(string contents)
    {
        var dir = Path.Combine(_root, "module", "Declarations", "AppModuleSiteMaps", "contoso_app");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "AppModuleSiteMap.xml"), contents);
    }

    /// <summary>An attribute with whatever metadata the case under test needs.</summary>
    private static string Attribute(string name, string type = "nvarchar", bool? isLogical = null)
    {
        var flags = string.Empty;
        if (isLogical != null) flags += $"<IsLogical>{(isLogical.Value ? 1 : 0)}</IsLogical>";
        var length = type == "nvarchar" ? "<MaxLength>50</MaxLength>" : string.Empty;
        return $"""<attribute PhysicalName="{name}"><Type>{type}</Type>{length}{flags}</attribute>""";
    }

    private static XElement Entity(string logicalName, params string[] attributes) =>
        XElement.Parse($"""
            <Entity>
              <Name LocalizedName="{logicalName}" OriginalName="{logicalName}">{logicalName}</Name>
              <EntityInfo><entity Name="{logicalName}"><attributes>
                {Attribute(logicalName + "id", "primarykey")}
                {string.Join("", attributes)}
              </attributes></entity></EntityInfo>
            </Entity>
            """);

    private ResolvedAppScope ScopeFor(DetailLevel detail, string[] tables, params string[] authorPrefixes)
    {
        var scope = new ResolvedAppScope { UniqueName = "contoso_app", Detail = detail };
        scope.SearchRoots.Add(_root);
        foreach (var table in tables) scope.TableLogicalNames.Add(table);
        foreach (var prefix in authorPrefixes) scope.AuthorPrefixes.Add(prefix);
        return scope;
    }

    private static Model.Table TableIn(Model.ParsedModel model, string name) =>
        model.tables.Single(t => t.LogicalName == name);

    private static bool Has(Model.ParsedModel model, string table, string column) =>
        TableIn(model, table).Rows.Any(r => string.Equals(r.Name, column, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void AColumnOnlyOneTablesFormRefersTo_IsKeptThere_AndDroppedOnTheOther()
    {
        // The whole point of the change. Both tables declare createdon; only one shows it.
        WriteArtefact("contoso_shown", "FormXml", "form.xml",
            """<form><control id="c" datafieldname="createdon" /></form>""");
        WriteArtefact("contoso_hidden", "FormXml", "form.xml",
            """<form><control id="c" datafieldname="contoso_other" /></form>""");

        var module = new Model.Module { ModuleName = "m" };
        module.entities.Add(Entity("contoso_shown", Attribute("createdon", "datetime")));
        module.entities.Add(Entity("contoso_hidden", Attribute("createdon", "datetime"),
                                                     Attribute("contoso_other")));

        var model = DataModelConverterService.ParseModules([module], ScopeFor(DetailLevel.Minimal, ["contoso_shown", "contoso_hidden"], "contoso"));

        Assert.True(Has(model, "contoso_shown", "createdon"));
        Assert.False(Has(model, "contoso_hidden", "createdon"));
    }

    [Fact]
    public void AnUnattributedReference_CannotRescueAPlatformColumn_ButDoesRescueAnAuthorsOne()
    {
        // A sitemap belongs to no single table, so it can only be matched by name -- and a
        // platform column's name is the same on every table in the org. Letting one rescue
        // createdon puts it straight back on every table, which is the defect being fixed.
        WriteArtefact("contoso_thing", "FormXml", "form.xml", "<form />");
        WriteSitemap("""<SiteMap><Note>createdon contoso_authored</Note></SiteMap>""");

        var module = new Model.Module { ModuleName = "m" };
        module.entities.Add(Entity("contoso_thing",
            Attribute("createdon", "datetime"),
            Attribute("contoso_authored")));

        var model = DataModelConverterService.ParseModules([module], ScopeFor(DetailLevel.Minimal, ["contoso_thing"], "contoso"));

        Assert.False(Has(model, "contoso_thing", "createdon"));
        Assert.True(Has(model, "contoso_thing", "contoso_authored"));
    }

    [Fact]
    public void WithNoPublisherPrefixAvailable_AnUnattributedReferenceStillKeepsAColumn()
    {
        // No solution manifest, so no publisher prefix to check a name against. Calling a
        // column the platform's on that basis would narrow the output on no evidence, so
        // the sitemap's reference counts for a name that would otherwise look like the
        // platform's.
        WriteArtefact("contoso_thing", "FormXml", "form.xml", "<form />");
        WriteSitemap("""<SiteMap><Note>mystery_column</Note></SiteMap>""");

        var module = new Model.Module { ModuleName = "m" };
        module.entities.Add(Entity("contoso_thing", Attribute("mystery_column")));

        var model = DataModelConverterService.ParseModules([module], ScopeFor(DetailLevel.Minimal, ["contoso_thing"]));

        Assert.True(Has(model, "contoso_thing", "mystery_column"));
    }

    [Fact]
    public void StateAndStatus_SurviveWithNothingReferringToThem()
    {
        // A state model describes the table whatever shows it, and is the one exception the
        // owner named to dropping the platform's own columns.
        WriteArtefact("contoso_thing", "FormXml", "form.xml", "<form />");

        var module = new Model.Module { ModuleName = "m" };
        module.entities.Add(Entity("contoso_thing",
            Attribute("statecode", "state"),
            Attribute("statuscode", "status")));

        var model = DataModelConverterService.ParseModules([module], ScopeFor(DetailLevel.Minimal, ["contoso_thing"], "contoso"));

        Assert.True(Has(model, "contoso_thing", "statecode"));
        Assert.True(Has(model, "contoso_thing", "statuscode"));
    }

    [Fact]
    public void DesignPlumbing_IsDroppedEvenWhereAFormRefersToIt()
    {
        // Logical columns are computed rather than stored, and process-flow bookkeeping is
        // not design. Both stay out whatever mentions them, and are reported as such rather
        // than as unreferenced.
        WriteArtefact("contoso_thing", "FormXml", "form.xml", """
            <form>
              <control id="a" datafieldname="owninguser" />
              <control id="b" datafieldname="stageid" />
            </form>
            """);

        var module = new Model.Module { ModuleName = "m" };
        module.entities.Add(Entity("contoso_thing",
            Attribute("owninguser", "lookup", isLogical: true),
            Attribute("stageid", "uniqueidentifier")));

        var scope = ScopeFor(DetailLevel.Minimal, ["contoso_thing"], "contoso");
        var model = DataModelConverterService.ParseModules([module], scope);

        Assert.False(Has(model, "contoso_thing", "owninguser"));
        Assert.False(Has(model, "contoso_thing", "stageid"));
        Assert.All(scope.DroppedColumns, c => Assert.Equal(DropReason.PlatformPlumbing, c.Reason));
    }

    [Fact]
    public void TheBaseCurrencyTwinIsDropped_AndTheColumnItShadowsIsKept()
    {
        // Both halves are marked as an author's, so nothing but the name pairing separates
        // the shadow the platform maintains from the column it shadows.
        WriteArtefact("contoso_thing", "FormXml", "form.xml", """
            <form>
              <control id="a" datafieldname="contoso_cost" />
              <control id="b" datafieldname="contoso_cost_base" />
            </form>
            """);

        var module = new Model.Module { ModuleName = "m" };
        module.entities.Add(Entity("contoso_thing",
            Attribute("contoso_cost", "money"),
            Attribute("contoso_cost_base", "money")));

        var model = DataModelConverterService.ParseModules([module], ScopeFor(DetailLevel.Minimal, ["contoso_thing"], "contoso"));

        Assert.True(Has(model, "contoso_thing", "contoso_cost"));
        Assert.False(Has(model, "contoso_thing", "contoso_cost_base"));
    }

    [Fact]
    public void AtFullDetail_EveryDesignOnlyRuleIsInert()
    {
        // The default must stay exactly what it converts today.
        var module = new Model.Module { ModuleName = "m" };
        module.entities.Add(Entity("contoso_thing",
            Attribute("createdon", "datetime"),
            Attribute("owninguser", "lookup", isLogical: true),
            Attribute("stageid", "uniqueidentifier"),
            Attribute("contoso_cost", "money"),
            Attribute("contoso_cost_base", "money")));

        var scope = ScopeFor(DetailLevel.Full, ["contoso_thing"], "contoso");
        var model = DataModelConverterService.ParseModules([module], scope);

        Assert.True(Has(model, "contoso_thing", "createdon"));
        Assert.True(Has(model, "contoso_thing", "owninguser"));
        Assert.True(Has(model, "contoso_thing", "stageid"));
        Assert.True(Has(model, "contoso_thing", "contoso_cost_base"));
        Assert.Empty(scope.DroppedColumns);
    }
}
