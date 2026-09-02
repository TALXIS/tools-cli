using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using TALXIS.CLI.Features.Data.DataModelConverter;
using TALXIS.CLI.Features.Data.DataModelConverter.AppScope;
using Model = TALXIS.CLI.Features.Data.DataModelConverter.Model;
using Xunit;

namespace TALXIS.CLI.Tests.Data.DataModelConverter;

/// <summary>
/// Resolving which tables a model-driven app is built on, from source alone. The shapes
/// exercised here are the ones real repositories actually contain: declarations under
/// "CDS" as well as "Declarations", one app declared across several files, and a folder
/// whose name differs in case from the UniqueName inside it.
/// </summary>
public class AppScopeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "txc-app-" + Path.GetRandomFileName());

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string WriteAppModule(string folderName, string uniqueName, string componentsXml, string declarationsFolder = "Declarations")
    {
        var dir = Path.Combine(_root, "module", declarationsFolder, "AppModules", folderName);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "AppModule_managed.xml");
        File.WriteAllText(file, $"""
            <AppModule>
              <UniqueName>{uniqueName}</UniqueName>
              <AppModuleComponents>
                {componentsXml}
              </AppModuleComponents>
            </AppModule>
            """);
        return file;
    }

    private static string Component(string type, string schemaName) =>
        $"""<AppModuleComponent type="{type}" schemaName="{schemaName}" />""";

    // ---- resolution ------------------------------------------------------------------

    [Fact]
    public void OnlyEntityComponentsContributeTables()
    {
        WriteAppModule("contoso_app", "contoso_app",
            Component("1", "contoso_thing")
            + Component("26", "some_view")      // saved query
            + Component("60", "some_form")      // system form
            + Component("62", "contoso_app")    // the app's own sitemap
            + Component("1", "account"));

        var scope = AppScopeResolver.Resolve([_root], "contoso_app");

        Assert.Equal(new[] { "account", "contoso_thing" }, scope.TableLogicalNames.OrderBy(x => x));
    }

    [Fact]
    public void DeclarationsUnderCdsFolder_AreStillFound()
    {
        // Older modules keep their declarations under "CDS" rather than "Declarations";
        // a search anchored on either folder name misses the other.
        WriteAppModule("contoso_app", "contoso_app", Component("1", "contoso_thing"), declarationsFolder: "CDS");

        var scope = AppScopeResolver.Resolve([_root], "contoso_app");

        Assert.Contains("contoso_thing", scope.TableLogicalNames);
    }

    [Fact]
    public void OneAppDeclaredAcrossSeveralFiles_UnionsItsComponents()
    {
        // A second area can contribute components to an app it does not own; the app's real
        // component set is the union of every file that declares it.
        WriteAppModule("contoso_app", "contoso_app", Component("1", "contoso_first"));
        var second = Path.Combine(_root, "other", "Declarations", "AppModules", "contoso_app");
        Directory.CreateDirectory(second);
        File.WriteAllText(Path.Combine(second, "AppModule_managed.xml"), """
            <AppModule>
              <UniqueName>contoso_app</UniqueName>
              <AppModuleComponents>
                <AppModuleComponent type="1" schemaName="contoso_second" solutionaction="Added" />
              </AppModuleComponents>
            </AppModule>
            """);

        var scope = AppScopeResolver.Resolve([_root], "contoso_app");

        Assert.Contains("contoso_first", scope.TableLogicalNames);
        Assert.Contains("contoso_second", scope.TableLogicalNames);
        Assert.Equal(2, scope.SourceFiles.Count);
    }

    [Fact]
    public void IdentityComesFromFileContent_NotTheFolderName()
    {
        // The folder is only a locator, and its casing can differ from the declared name.
        WriteAppModule("Contoso_App", "contoso_app", Component("1", "contoso_thing"));

        var scope = AppScopeResolver.Resolve([_root], "contoso_app");

        Assert.Contains("contoso_thing", scope.TableLogicalNames);
    }

    [Fact]
    public void SiteMapEntities_AreIncluded_FromBothTheAttributeAndTheUrl()
    {
        WriteAppModule("contoso_app", "contoso_app", Component("1", "contoso_thing"));
        var dir = Path.Combine(_root, "module", "Declarations", "AppModuleSiteMaps", "contoso_app");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "AppModuleSiteMap_managed.xml"), """
            <AppModuleSiteMap>
              <SiteMapUniqueName>contoso_app</SiteMapUniqueName>
              <SubArea Id="a" Entity="contoso_viaattribute" />
              <SubArea Id="b" Url="/main.aspx?pagetype=entitylist&amp;etn=contoso_viaurl&amp;viewid={x}" />
            </AppModuleSiteMap>
            """);

        var scope = AppScopeResolver.Resolve([_root], "contoso_app");

        Assert.Contains("contoso_viaattribute", scope.TableLogicalNames);
        Assert.Contains("contoso_viaurl", scope.TableLogicalNames);
    }

    [Fact]
    public void UnknownApp_FailsWithTheNamesItDidFind()
    {
        WriteAppModule("contoso_app", "contoso_app", Component("1", "contoso_thing"));

        var ex = Assert.Throws<InvalidOperationException>(() => AppScopeResolver.Resolve([_root], "contoso_typo"));

        Assert.Contains("contoso_app", ex.Message);
    }

    // ---- filtering -------------------------------------------------------------------

    private static XElement Entity(string logicalName) =>
        XElement.Parse($"""
            <Entity>
              <Name LocalizedName="{logicalName}" OriginalName="{logicalName}">{logicalName}</Name>
              <EntityInfo><entity Name="{logicalName}"><attributes>
                <attribute PhysicalName="{logicalName}id"><Type>primarykey</Type></attribute>
                <attribute PhysicalName="{logicalName}_lookup"><Type>lookup</Type></attribute>
              </attributes></entity></EntityInfo>
            </Entity>
            """);

    private static XElement OneToMany(string child, string attr, string parent) =>
        XElement.Parse($"""
            <EntityRelationship Name="rel_{child}_{parent}">
              <EntityRelationshipType>OneToMany</EntityRelationshipType>
              <ReferencingEntityName>{child}</ReferencingEntityName>
              <ReferencedEntityName>{parent}</ReferencedEntityName>
              <ReferencingAttributeName>{attr}</ReferencingAttributeName>
            </EntityRelationship>
            """);

    [Fact]
    public void TablesOutsideTheApp_AreDropped_AndDoNotReturnAsRelationshipStubs()
    {
        // The ordering that matters: filtering after relationships were built would let a
        // relationship among the dropped tables synthesise them straight back as stubs.
        var module = new Model.Module { ModuleName = "test" };
        module.entities.AddRange([Entity("contoso_inapp"), Entity("contoso_elsewhere"), Entity("contoso_alsoelsewhere")]);
        module.relationships.Add(OneToMany("contoso_elsewhere", "contoso_elsewhere_lookup", "contoso_alsoelsewhere"));

        var scope = new ResolvedAppScope { UniqueName = "contoso_app" };
        scope.TableLogicalNames.Add("contoso_inapp");

        var model = DataModelConverterService.ParseModules([module], scope);

        Assert.Contains(model.tables, t => t.LogicalName == "contoso_inapp");
        Assert.DoesNotContain(model.tables, t => t.LogicalName == "contoso_elsewhere");
        Assert.DoesNotContain(model.tables, t => t.LogicalName == "contoso_alsoelsewhere");
    }

    [Fact]
    public void ALookupOutOfTheApp_StillTerminates_SoTheEdgeIsNotLost()
    {
        var module = new Model.Module { ModuleName = "test" };
        module.entities.AddRange([Entity("contoso_inapp"), Entity("contoso_outside")]);
        module.relationships.Add(OneToMany("contoso_inapp", "contoso_inapp_lookup", "contoso_outside"));

        var scope = new ResolvedAppScope { UniqueName = "contoso_app" };
        scope.TableLogicalNames.Add("contoso_inapp");

        var model = DataModelConverterService.ParseModules([module], scope);

        Assert.Contains(model.relationships, r => r.LeftSideTable?.LogicalName == "contoso_inapp");

        // NotInApp rather than NotInSolution: this input does declare the table, so it is
        // outside the app rather than missing from the solution.
        Assert.Contains(model.tables, t => t.LogicalName == "contoso_outside" && t.Type == Model.TableType.NotInApp);
    }

    [Fact]
    public void WithoutAnAppScope_NothingIsFiltered()
    {
        var module = new Model.Module { ModuleName = "test" };
        module.entities.AddRange([Entity("contoso_a"), Entity("contoso_b")]);

        var model = DataModelConverterService.ParseModules([module]);

        Assert.Contains(model.tables, t => t.LogicalName == "contoso_a");
        Assert.Contains(model.tables, t => t.LogicalName == "contoso_b");
    }
}
