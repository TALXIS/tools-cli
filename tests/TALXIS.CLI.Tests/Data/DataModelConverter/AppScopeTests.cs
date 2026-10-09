using System;
using System.IO;
using System.Linq;
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
public class AppScopeTests
{
    private static Model.Module ModuleOf(string[] entities, params string[] relationships)
    {
        var module = new Model.Module { ModuleName = "test" };
        module.entities.AddRange(entities.Select(XElement.Parse));
        module.relationships.AddRange(relationships.Select(XElement.Parse));
        return module;
    }

    private static XElement OptionSetOf(string name) =>
        XElement.Parse($"""
            <optionset Name="{name}">
              <OptionSetType>picklist</OptionSetType>
              <options><option value="1"><labels><label description="One" languagecode="1033" /></labels></option></options>
            </optionset>
            """);

    private static ResolvedAppScope ScopeOf(params string[] tables)
    {
        var scope = new ResolvedAppScope { UniqueName = "contoso_app" };
        scope.TableLogicalNames.UnionWith(tables);
        return scope;
    }

    private static string[] Names(Model.ParsedModel model) =>
        [.. model.tables.Select(table => table.LogicalName).Order(StringComparer.Ordinal)];

    private static string Shop(TestTree tree, string declarations)
    {
        tree.Write($"{declarations}/Entities/contoso_order/Entity.xml",
            TestTree.Entity("contoso_order", TestTree.Attr("contoso_total", "int"), TestTree.Picklist("contoso_kind")));
        tree.Write($"{declarations}/Entities/contoso_customer/Entity.xml",
            TestTree.Entity("contoso_customer", TestTree.Attr("contoso_name", "nvarchar")));
        tree.Write($"{declarations}/Entities/contoso_audit/Entity.xml",
            TestTree.Entity("contoso_audit", TestTree.Picklist("contoso_level")));
        tree.OptionSet(declarations, "contoso_kind");
        tree.OptionSet(declarations, "contoso_level");
        tree.Relationships(declarations,
            TestTree.OneToMany("contoso_order", "contoso_customerid", "contoso_customer"),
            TestTree.OneToMany("contoso_audit", "contoso_orderid", "contoso_order"));
        return tree.Full(declarations);
    }

    private static string ShopApp(TestTree tree, string declarations) =>
        tree.AppModule(declarations, "contoso_app", "contoso_app",
            TestTree.Component("1", "contoso_order"),
            TestTree.Component("1", "contoso_customer"),
            TestTree.Component("60", "contoso_form"));

    private static void AssertScopedToTheShopApp(string dbml)
    {
        Assert.Contains("table contoso_order ", dbml);
        Assert.Contains("table contoso_customer ", dbml);
        Assert.DoesNotContain("contoso_audit", dbml);
        Assert.Contains("\"contoso_order\".\"contoso_customerid\"", dbml);
        Assert.Contains("Enum contoso_kind ", dbml);
        Assert.DoesNotContain("contoso_level", dbml);
    }

    private static async Task<int> Run(DataModelConvertCliCommand command)
    {
        using (OutputWriter.RedirectTo(new StringWriter()))
        {
            return await command.RunAsync();
        }
    }

    private static async Task<int> RunFrom(string directory, DataModelConvertCliCommand command)
    {
        var previous = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(directory);
            return await Run(command);
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }

    [Fact]
    public void TablesOutsideTheApp_AreDropped_AndDoNotReturnAsRelationshipStubs()
    {
        var module = ModuleOf(
            [TestTree.Entity("contoso_inapp"), TestTree.Entity("contoso_elsewhere"), TestTree.Entity("contoso_alsoelsewhere")],
            TestTree.OneToMany("contoso_elsewhere", "contoso_elsewhere_lookup", "contoso_alsoelsewhere"));

        var model = DataModelConverterService.ParseModules([module], ScopeOf("contoso_inapp"));

        Assert.Equal(["contoso_inapp"], Names(model));
        Assert.Empty(model.relationships);
    }

    [Fact]
    public void ALookupOutOfTheApp_StillTerminates_SoTheEdgeIsNotLost()
    {
        var module = ModuleOf(
            [TestTree.Entity("contoso_inapp"), TestTree.Entity("contoso_outside")],
            TestTree.OneToMany("contoso_inapp", "contoso_inapp_lookup", "contoso_outside"));

        var model = DataModelConverterService.ParseModules([module], ScopeOf("contoso_inapp"));

        Assert.Equal(["contoso_inapp", "contoso_outside"], Names(model));
        Assert.Single(model.relationships, r => r.LeftSideTable.LogicalName == "contoso_inapp" && r.RighSideTable.LogicalName == "contoso_outside");
        Assert.Equal(Model.TableType.NotInApp, model.tables.Single(table => table.LogicalName == "contoso_outside").Type);
    }

    [Fact]
    public void AManyToManyStub_IsOutsideTheApp_WhenAnInputDeclaresTheTable()
    {
        var module = ModuleOf(
            [TestTree.Entity("contoso_inapp"), TestTree.Entity("contoso_outside")],
            TestTree.ManyToMany("contoso_link", "contoso_inapp", "contoso_outside"));

        var model = DataModelConverterService.ParseModules([module], ScopeOf("contoso_inapp"));

        Assert.Equal(Model.TableType.NotInApp, model.tables.Single(table => table.LogicalName == "contoso_outside").Type);
        Assert.Equal(Model.TableType.ConnectionTable, model.tables.Single(table => table.LogicalName == "contoso_link").Type);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AStubForATableNoInputDeclares_IsMissingFromTheSolution(bool scoped)
    {
        var module = ModuleOf(
            [TestTree.Entity("contoso_inapp")],
            TestTree.OneToMany("contoso_inapp", "contoso_inapp_lookup", "contoso_ghost"));

        var model = DataModelConverterService.ParseModules([module], scoped ? ScopeOf("contoso_inapp") : null);

        Assert.Equal(Model.TableType.NotInSolution, model.tables.Single(table => table.LogicalName == "contoso_ghost").Type);
    }

    [Fact]
    public void TheDiagramTellsATableOutsideTheAppFromOneMissingFromTheSolution()
    {
        using var tree = new TestTree();
        var model = Shop(tree, "Model/Declarations");
        var ghosts = tree.Relationships("Ghosts/Declarations", TestTree.OneToMany("contoso_order", "contoso_ghostid", "contoso_ghost"));
        tree.AppModule("Apps/Declarations", "contoso_app", "contoso_app", TestTree.Component("1", "contoso_order"));
        var output = Path.Combine(tree.Output, "solution.dbml");

        DataModelConverterService.ConvertModel([model, ghosts], "dbml", output, "contoso_app", [tree.Full("Apps")]);

        var dbml = File.ReadAllText(output);
        Assert.Contains("table contoso_customer [headercolor: #7f8c8d] //declared outside this app", dbml);
        Assert.Contains("table contoso_ghost [headercolor: #c0392b]", dbml);
    }

    [Fact]
    public void ALookupFromOutsideTheApp_IsDropped_EvenWhenItPointsIntoTheApp()
    {
        var module = ModuleOf(
            [TestTree.Entity("contoso_inapp"), TestTree.Entity("contoso_outside")],
            TestTree.OneToMany("contoso_outside", "contoso_outside_lookup", "contoso_inapp"));

        var model = DataModelConverterService.ParseModules([module], ScopeOf("contoso_inapp"));

        Assert.Equal(["contoso_inapp"], Names(model));
        Assert.Empty(model.relationships);
    }

    [Theory]
    [InlineData("contoso_inapp", "contoso_outside", true)]
    [InlineData("contoso_outside", "contoso_inapp", true)]
    [InlineData("contoso_outside", "contoso_alsooutside", false)]
    public void AManyToMany_IsKeptWhenEitherSideIsInTheApp(string first, string second, bool kept)
    {
        var module = ModuleOf(
            [TestTree.Entity("contoso_inapp"), TestTree.Entity("contoso_outside"), TestTree.Entity("contoso_alsooutside")],
            TestTree.ManyToMany("contoso_link", first, second));

        var model = DataModelConverterService.ParseModules([module], ScopeOf("contoso_inapp"));

        Assert.Equal(kept, model.tables.Any(table => table.LogicalName == "contoso_link"));
        Assert.Equal(kept, model.relationships.Count > 0);
        Assert.DoesNotContain(model.tables, table => table.LogicalName == "contoso_alsooutside");
    }

    [Fact]
    public void OptionSetsOnlyDroppedTablesUse_AreNotEmitted()
    {
        var module = ModuleOf(
            [TestTree.Entity("contoso_inapp", TestTree.Picklist("contoso_kept")), TestTree.Entity("contoso_elsewhere", TestTree.Picklist("contoso_dropped"))]);
        module.optionsets.AddRange([OptionSetOf("contoso_kept"), OptionSetOf("contoso_dropped")]);

        var model = DataModelConverterService.ParseModules([module], ScopeOf("contoso_inapp"));

        Assert.Equal(["contoso_kept"], model.optionSets.Select(optionSet => optionSet.LocalizedName));
    }

    [Fact]
    public void WithoutAnAppScope_NothingIsFiltered()
    {
        var module = ModuleOf([TestTree.Entity("contoso_a"), TestTree.Entity("contoso_b")]);

        Assert.Equal(["contoso_a", "contoso_b"], Names(DataModelConverterService.ParseModules([module])));
    }

    [Fact]
    public void ConvertModel_NarrowsTheOutputToTheApp()
    {
        using var tree = new TestTree();
        var model = Shop(tree, "Model/Declarations");
        ShopApp(tree, "Apps/Declarations");
        var output = Path.Combine(tree.Output, "solution.dbml");

        DataModelConverterService.ConvertModel([model], "dbml", output, "contoso_app", [tree.Full("Apps")]);

        AssertScopedToTheShopApp(File.ReadAllText(output));
    }

    [Fact]
    public void ConvertModel_WithoutAnApp_EmitsEverythingTheInputsDeclare()
    {
        using var tree = new TestTree();
        var model = Shop(tree, "Model/Declarations");
        var output = Path.Combine(tree.Output, "solution.dbml");

        DataModelConverterService.ConvertModel([model], "dbml", output);

        var dbml = File.ReadAllText(output);
        Assert.Contains("table contoso_audit ", dbml);
        Assert.Contains("Enum contoso_level ", dbml);
    }

    [Fact]
    public void ConvertModel_SearchesTheInputsForTheApp_WhenNoSearchRootIsGiven()
    {
        using var tree = new TestTree();
        var declarations = Shop(tree, "Both/Declarations");
        ShopApp(tree, "Both/Declarations");
        var output = Path.Combine(tree.Output, "solution.dbml");

        DataModelConverterService.ConvertModel([declarations], "dbml", output, "contoso_app");

        AssertScopedToTheShopApp(File.ReadAllText(output));
    }

    [Fact]
    public void ConvertModel_SearchesAProjectFoldersDeclarations_WhereverTheProjectPointsAtThem()
    {
        using var tree = new TestTree();
        Shop(tree, "Shared/Declarations");
        ShopApp(tree, "Shared/Declarations");
        tree.Write("Project/Model.csproj", "<Project><PropertyGroup><SolutionRootPath>../Shared/Declarations</SolutionRootPath></PropertyGroup></Project>");
        var output = Path.Combine(tree.Output, "solution.dbml");

        DataModelConverterService.ConvertModel([tree.Full("Project")], "dbml", output, "contoso_app");

        AssertScopedToTheShopApp(File.ReadAllText(output));
    }

    [Fact]
    public void ConvertModel_DoesNotSearchTheInputs_WhenSearchRootsAreGiven()
    {
        using var tree = new TestTree();
        var declarations = Shop(tree, "Both/Declarations");
        ShopApp(tree, "Both/Declarations");
        Directory.CreateDirectory(tree.Full("Elsewhere"));
        var output = Path.Combine(tree.Output, "solution.dbml");

        Assert.Throws<ArgumentException>(() =>
            DataModelConverterService.ConvertModel([declarations], "dbml", output, "contoso_app", [tree.Full("Elsewhere")]));
    }

    [Fact]
    public void ConvertModel_LeavesOutATableAFragmentRemovesFromTheApp()
    {
        using var tree = new TestTree();
        var model = Shop(tree, "Model/Declarations");
        tree.Write("Model/Declarations/Entities/contoso_note/Entity.xml", TestTree.Entity("contoso_note", TestTree.Attr("contoso_text", "nvarchar")));
        tree.AppModule("Apps/Declarations", "contoso_app", "contoso_app", TestTree.Component("1", "contoso_order"), TestTree.Component("1", "contoso_note"));
        tree.AppModule("Fragments/Declarations", "contoso_app", "contoso_app", TestTree.Component("1", "contoso_note", "Removed"));
        var output = Path.Combine(tree.Output, "solution.dbml");

        DataModelConverterService.ConvertModel([model], "dbml", output, "contoso_app", [tree.Full("Apps")]);
        Assert.Contains("table contoso_note ", File.ReadAllText(output));

        DataModelConverterService.ConvertModel([model], "dbml", output, "contoso_app", [tree.Full("Apps"), tree.Full("Fragments")]);
        Assert.DoesNotContain("contoso_note", File.ReadAllText(output));
    }

    [Fact]
    public void ConvertModel_RejectsAnUnknownApp_AndWritesNothing()
    {
        using var tree = new TestTree();
        var model = Shop(tree, "Model/Declarations");
        ShopApp(tree, "Apps/Declarations");
        var output = Path.Combine(tree.Output, "solution.dbml");

        var ex = Assert.Throws<ArgumentException>(() =>
            DataModelConverterService.ConvertModel([model], "dbml", output, "contoso_typo", [tree.Root]));

        Assert.Contains("contoso_app", ex.Message);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task TheCommand_ScopesToTheAppFoundUnderTheRoot()
    {
        using var tree = new TestTree();
        Shop(tree, "Contoso/Shop/Model/Declarations");
        ShopApp(tree, "Contoso/Shop/Apps/Declarations");
        tree.Declarations("Contoso/Other/Declarations", null, "contoso_unrelated");

        var exit = await Run(new DataModelConvertCliCommand
        {
            Roots = [tree.Root],
            AppUniqueName = "contoso_app",
            TargetFormat = "dbml",
            OutputDirectory = tree.Output,
        });

        Assert.Equal(0, exit);
        var dbml = tree.Dbml();
        AssertScopedToTheShopApp(dbml);
        Assert.DoesNotContain("contoso_unrelated", dbml);
    }

    [Fact]
    public async Task TheCommand_SearchesTheInputsForTheApp_WhateverTheCurrentDirectory()
    {
        using var tree = new TestTree();
        var declarations = Shop(tree, "Both/Declarations");
        ShopApp(tree, "Both/Declarations");

        var exit = await Run(new DataModelConvertCliCommand
        {
            InputPaths = [declarations],
            AppUniqueName = "contoso_app",
            TargetFormat = "dbml",
            OutputDirectory = tree.Output,
        });

        Assert.Equal(0, exit);
        AssertScopedToTheShopApp(tree.Dbml());
    }

    [Fact]
    public async Task TheCommand_DoesNotSearchTheRepositoryAroundTheCurrentDirectory()
    {
        using var tree = new TestTree();
        Directory.CreateDirectory(tree.Full(".git"));
        var model = Shop(tree, "Model/Declarations");
        ShopApp(tree, "Apps/Declarations");

        var exit = await RunFrom(tree.Full("Model"), new DataModelConvertCliCommand
        {
            InputPaths = [model],
            AppUniqueName = "contoso_app",
            TargetFormat = "dbml",
            OutputDirectory = tree.Output,
        });

        Assert.Equal(2, exit);
        Assert.False(File.Exists(Path.Combine(tree.Output, "solution.dbml")));
    }

    [Fact]
    public async Task TheCommand_TreatsAnUnknownAppAsAValidationError_AndTouchesNothing()
    {
        using var tree = new TestTree();
        Shop(tree, "Model/Declarations");
        ShopApp(tree, "Apps/Declarations");
        tree.Write(".gitignore", "bin/\n");
        var exports = tree.Full("exports");

        var exit = await Run(new DataModelConvertCliCommand
        {
            Roots = [tree.Root],
            AppUniqueName = "contoso_typo",
            TargetFormat = "dbml",
            OutputDirectory = exports,
        });

        Assert.Equal(2, exit);
        Assert.False(Directory.Exists(exports));
        Assert.Equal("bin/\n", File.ReadAllText(tree.Full(".gitignore")));
    }

    [Fact]
    public async Task TheCommand_CreatesTheOutputFolderAndIgnoresIt_OnceTheConversionSucceeds()
    {
        using var tree = new TestTree();
        Shop(tree, "Model/Declarations");
        ShopApp(tree, "Apps/Declarations");
        tree.Write(".gitignore", "bin/\n");
        var exports = tree.Full("exports");

        var exit = await Run(new DataModelConvertCliCommand
        {
            Roots = [tree.Root],
            AppUniqueName = "contoso_app",
            TargetFormat = "dbml",
            OutputDirectory = exports,
        });

        Assert.Equal(0, exit);
        Assert.True(File.Exists(Path.Combine(exports, "solution.dbml")));
        Assert.Contains("exports/", File.ReadAllText(tree.Full(".gitignore")));
    }
}
