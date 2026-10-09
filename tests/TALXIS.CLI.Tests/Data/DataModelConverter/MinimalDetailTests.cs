using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Features.Data.DataModelConverter;
using TALXIS.CLI.Features.Data.DataModelConverter.AppScope;
using Model = TALXIS.CLI.Features.Data.DataModelConverter.Model;
using Xunit;

namespace TALXIS.CLI.Tests.Data.DataModelConverter;

public class MinimalDetailTests
{
    private static Model.Module ModuleOf(string? prefix, params XElement[] entities)
    {
        var module = new Model.Module { ModuleName = "test", CustomizationPrefix = prefix };
        module.entities.AddRange(entities);
        return module;
    }

    private static XElement Entity(string logicalName, params string[] attributes) =>
        XElement.Parse(TestTree.Entity(logicalName, attributes));

    private static ResolvedAppScope MinimalScope(TestTree tree, params string[] tables)
    {
        var scope = new ResolvedAppScope { UniqueName = "contoso_app", Detail = DetailLevel.Minimal };
        scope.SearchRoots.Add(tree.Root);
        scope.TableLogicalNames.UnionWith(tables);
        return scope;
    }

    private static void Form(TestTree tree, string table, string content) =>
        tree.Write($"Model/Declarations/Entities/{table}/FormXml/main/form.xml", content);

    private static bool Has(Model.ParsedModel model, string table, string column) =>
        model.tables.Single(t => t.LogicalName == table).Rows.Any(row => row.Name == column);

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    [Fact]
    public void AColumnAFormRefersTo_IsKept_AndOneNothingRefersTo_IsDroppedAndReported()
    {
        using var tree = new TestTree();
        Form(tree, "contoso_order", """<form><control datafieldname="contoso_shown" /></form>""");
        var scope = MinimalScope(tree, "contoso_order");

        var model = DataModelConverterService.ParseModules(
            [ModuleOf("contoso", Entity("contoso_order", TestTree.Attr("contoso_shown", "int"), TestTree.Attr("contoso_unused", "int")))], scope);

        Assert.True(Has(model, "contoso_order", "contoso_shown"));
        Assert.False(Has(model, "contoso_order", "contoso_unused"));
        Assert.Equal([new DroppedColumn("contoso_order", "contoso_unused", DropReason.NoReferenceFound)], scope.DroppedColumns);
    }

    [Fact]
    public void AColumnOnlyOneTablesFormRefersTo_IsKeptThere_AndDroppedOnTheOther()
    {
        using var tree = new TestTree();
        Form(tree, "contoso_shown", """<form><control datafieldname="createdon" /></form>""");
        Form(tree, "contoso_hidden", """<form><control datafieldname="contoso_other" /></form>""");

        var model = DataModelConverterService.ParseModules(
            [ModuleOf("contoso",
                Entity("contoso_shown", TestTree.Attr("createdon", "datetime")),
                Entity("contoso_hidden", TestTree.Attr("createdon", "datetime"), TestTree.Attr("contoso_other", "int")))],
            MinimalScope(tree, "contoso_shown", "contoso_hidden"));

        Assert.True(Has(model, "contoso_shown", "createdon"));
        Assert.False(Has(model, "contoso_hidden", "createdon"));
    }

    [Fact]
    public void AFileOutsideAnyTable_KeepsAnAuthorsColumn_ButCannotRescueAPlatformOne()
    {
        using var tree = new TestTree();
        Form(tree, "contoso_order", "<form />");
        tree.SiteMap("Model/Declarations", "contoso_app", "contoso_app", """<SubArea Note="createdon contoso_authored" />""");

        var model = DataModelConverterService.ParseModules(
            [ModuleOf("contoso", Entity("contoso_order", TestTree.Attr("createdon", "datetime"), TestTree.Attr("contoso_authored", "int")))],
            MinimalScope(tree, "contoso_order"));

        Assert.False(Has(model, "contoso_order", "createdon"));
        Assert.True(Has(model, "contoso_order", "contoso_authored"));
    }

    [Fact]
    public void AColumnOnlyCodeMentions_IsKept()
    {
        using var tree = new TestTree();
        Form(tree, "contoso_order", "<form />");
        tree.Write("Plugins/Handler.cs", """var total = entity.GetAttributeValue<int>("contoso_fromcode");""");

        var model = DataModelConverterService.ParseModules(
            [ModuleOf("contoso", Entity("contoso_order", TestTree.Attr("contoso_fromcode", "int")))],
            MinimalScope(tree, "contoso_order"));

        Assert.True(Has(model, "contoso_order", "contoso_fromcode"));
    }

    [Fact]
    public void AnEntityDeclaration_DoesNotKeepItsOwnColumns()
    {
        using var tree = new TestTree();
        Form(tree, "contoso_order", "<form />");
        tree.Write("Model/Declarations/Entities/contoso_order/Entity.xml", TestTree.Entity("contoso_order", TestTree.Attr("contoso_declared", "int")));

        var model = DataModelConverterService.ParseModules(
            [ModuleOf("contoso", Entity("contoso_order", TestTree.Attr("contoso_declared", "int")))],
            MinimalScope(tree, "contoso_order"));

        Assert.False(Has(model, "contoso_order", "contoso_declared"));
    }

    [Fact]
    public void KeysAndStateColumns_SurviveWithNothingReferringToThem()
    {
        using var tree = new TestTree();
        Form(tree, "contoso_order", "<form />");

        var model = DataModelConverterService.ParseModules(
            [ModuleOf("contoso", Entity("contoso_order", TestTree.Attr("statecode", "state"), TestTree.Attr("statuscode", "status"), TestTree.Attr("contoso_unused", "int")))],
            MinimalScope(tree, "contoso_order"));

        Assert.True(Has(model, "contoso_order", "contoso_orderid"));
        Assert.True(Has(model, "contoso_order", "statecode"));
        Assert.True(Has(model, "contoso_order", "statuscode"));
        Assert.False(Has(model, "contoso_order", "contoso_unused"));
    }

    [Fact]
    public void AColumnARelationshipUses_Survives_AndEveryTargetStillRenders()
    {
        using var tree = new TestTree();
        Form(tree, "contoso_child", "<form />");
        var module = ModuleOf("contoso", Entity("contoso_child", TestTree.Attr("contoso_parentid", "lookup")), Entity("contoso_parent"));
        module.relationships.Add(XElement.Parse(TestTree.OneToMany("contoso_child", "contoso_parentid", "contoso_parent")));

        var model = DataModelConverterService.ParseModules([module], MinimalScope(tree, "contoso_child", "contoso_parent"));

        Assert.True(Has(model, "contoso_child", "contoso_parentid"));
        Assert.Null(Record.Exception(() => DataModelConverterService.ConvertToDBML(model)));
        Assert.Null(Record.Exception(() => DataModelConverterService.ConvertToSQL(model)));
        Assert.Null(Record.Exception(() => DataModelConverterService.ConvertToEDSSQL(model)));
        Assert.Null(Record.Exception(() => DataModelConverterService.ConvertToEDMX(model)));
    }

    [Fact]
    public void LogicalAndProcessFlowColumns_AreDroppedEvenWhereAFormNamesThem_AsPlumbing()
    {
        using var tree = new TestTree();
        Form(tree, "contoso_order", """<form><control datafieldname="owningname" /><control datafieldname="stageid" /></form>""");
        var scope = MinimalScope(tree, "contoso_order");

        var model = DataModelConverterService.ParseModules(
            [ModuleOf("contoso", Entity("contoso_order", TestTree.Attr("owningname", "nvarchar", isLogical: true), TestTree.Attr("stageid", "uniqueidentifier")))],
            scope);

        Assert.False(Has(model, "contoso_order", "owningname"));
        Assert.False(Has(model, "contoso_order", "stageid"));
        Assert.All(scope.DroppedColumns, column => Assert.Equal(DropReason.PlatformPlumbing, column.Reason));
        Assert.Equal(2, scope.DroppedColumns.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheBaseCurrencyTwin_IsDropped_WhateverOrderTheColumnsAreDeclaredIn(bool twinFirst)
    {
        using var tree = new TestTree();
        Form(tree, "contoso_order", """<form><control datafieldname="contoso_cost_base" /></form>""");
        string[] columns = twinFirst
            ? [TestTree.Attr("contoso_cost_base", "money"), TestTree.Attr("contoso_cost", "money")]
            : [TestTree.Attr("contoso_cost", "money"), TestTree.Attr("contoso_cost_base", "money")];
        var scope = MinimalScope(tree, "contoso_order");

        var model = DataModelConverterService.ParseModules([ModuleOf("contoso", Entity("contoso_order", columns))], scope);

        Assert.False(Has(model, "contoso_order", "contoso_cost_base"));
        Assert.Contains(new DroppedColumn("contoso_order", "contoso_cost_base", DropReason.PlatformPlumbing), scope.DroppedColumns);
    }

    [Fact]
    public void AMoneyColumnWithoutASiblingIsNotATwin()
    {
        using var tree = new TestTree();
        Form(tree, "contoso_order", """<form><control datafieldname="contoso_cost_base" /></form>""");

        var model = DataModelConverterService.ParseModules(
            [ModuleOf("contoso", Entity("contoso_order", TestTree.Attr("contoso_cost_base", "money")))],
            MinimalScope(tree, "contoso_order"));

        Assert.True(Has(model, "contoso_order", "contoso_cost_base"));
    }

    [Theory]
    [InlineData("node_modules")]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData(".git")]
    public void AFileInAThrowawayDirectory_DoesNotKeepAColumn(string directory)
    {
        using var tree = new TestTree();
        Form(tree, "contoso_order", "<form />");
        tree.Write($"Model/{directory}/package/index.js", "const column = 'contoso_buried';");
        tree.Write("Model/src/index.js", "const column = 'contoso_found';");

        var model = DataModelConverterService.ParseModules(
            [ModuleOf("contoso", Entity("contoso_order", TestTree.Attr("contoso_buried", "int"), TestTree.Attr("contoso_found", "int")))],
            MinimalScope(tree, "contoso_order"));

        Assert.True(Has(model, "contoso_order", "contoso_found"));
        Assert.False(Has(model, "contoso_order", "contoso_buried"));
    }

    [Fact]
    public void AFolderNamedEntitiesAboveTheRoot_DoesNotChangeWhichTableAFileBelongsTo()
    {
        using var tree = new TestTree();
        tree.Write("Entities/contoso_wrong/Model/Declarations/Entities/contoso_order/FormXml/main/form.xml", """<form><control datafieldname="createdon" /></form>""");
        var scope = MinimalScope(tree, "contoso_order");
        scope.SearchRoots[0] = tree.Full("Entities/contoso_wrong/Model");

        var model = DataModelConverterService.ParseModules(
            [ModuleOf("contoso", Entity("contoso_order", TestTree.Attr("createdon", "datetime")))], scope);

        Assert.True(Has(model, "contoso_order", "createdon"));
    }

    [Fact]
    public void AReferencingFolderNameAboveTheRoot_DoesNotMakeEveryFileCount()
    {
        using var tree = new TestTree();
        tree.Write("FormXml/Model/Declarations/Entities/contoso_order/FormXml/main/form.xml", "<form />");
        tree.Write("FormXml/Model/Other/notes.xml", "<notes>contoso_unrelated</notes>");
        var scope = MinimalScope(tree, "contoso_order");
        scope.SearchRoots[0] = tree.Full("FormXml/Model");

        var model = DataModelConverterService.ParseModules(
            [ModuleOf("contoso", Entity("contoso_order", TestTree.Attr("contoso_unrelated", "int")))], scope);

        Assert.False(Has(model, "contoso_order", "contoso_unrelated"));
    }

    [Fact]
    public void WithNoPublisherPrefix_AFileNamingAPlatformColumnKeepsIt()
    {
        using var tree = new TestTree();
        Form(tree, "contoso_order", "<form />");
        tree.SiteMap("Model/Declarations", "contoso_app", "contoso_app", """<SubArea Note="createdon" />""");

        var model = DataModelConverterService.ParseModules(
            [ModuleOf(null, Entity("contoso_order", TestTree.Attr("createdon", "datetime")))],
            MinimalScope(tree, "contoso_order"));

        Assert.True(Has(model, "contoso_order", "createdon"));
    }

    [Fact]
    public void WithNoPublisherPrefix_TheFilterWarns()
    {
        using var tree = new TestTree();
        var logger = new RecordingLogger();

        AttributeReferenceFilter.Apply([], [], MinimalScope(tree), [], logger);

        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("publisher prefix"));
    }

    [Fact]
    public void WithAPublisherPrefix_TheFilterDoesNotWarnAboutIt()
    {
        using var tree = new TestTree();
        var logger = new RecordingLogger();

        AttributeReferenceFilter.Apply([], [], MinimalScope(tree), ["contoso"], logger);

        Assert.DoesNotContain(logger.Entries, entry => entry.Message.Contains("publisher prefix"));
    }

    [Fact]
    public void ATableWithNoFilesOfItsOwn_IsNamedInAWarning()
    {
        using var tree = new TestTree();
        Form(tree, "contoso_shown", "<form />");
        var logger = new RecordingLogger();
        var tables = DataModelConverterService.ParseEntities([ModuleOf("contoso", Entity("contoso_shown"), Entity("contoso_bare"))]);

        AttributeReferenceFilter.Apply(tables, [], MinimalScope(tree, "contoso_shown", "contoso_bare"), ["contoso"], logger);

        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("contoso_bare") && !entry.Message.Contains("contoso_shown"));
    }

    [Fact]
    public void AtFullDetail_ColumnsAreLeftAlone()
    {
        using var tree = new TestTree();
        Form(tree, "contoso_order", "<form />");
        var scope = MinimalScope(tree, "contoso_order");
        scope.Detail = DetailLevel.Full;

        var model = DataModelConverterService.ParseModules(
            [ModuleOf("contoso", Entity("contoso_order", TestTree.Attr("contoso_unused", "int"), TestTree.Attr("stageid", "uniqueidentifier", isLogical: true)))],
            scope);

        Assert.True(Has(model, "contoso_order", "contoso_unused"));
        Assert.True(Has(model, "contoso_order", "stageid"));
        Assert.Empty(scope.DroppedColumns);
    }

    [Fact]
    public void ConvertModel_WritesTheNarrowedDiagram_AndReturnsWhatItLeftOut()
    {
        using var tree = new TestTree();
        var model = tree.OrderApp();
        var output = Path.Combine(tree.Output, "solution.dbml");

        var dropped = DataModelConverterService.ConvertModel([model], "dbml", output, "contoso_app", detail: DetailLevel.Minimal);

        var dbml = File.ReadAllText(output);
        Assert.Contains("  contoso_total ", dbml);
        Assert.Contains("  createdon ", dbml);
        Assert.Contains("  contoso_note ", dbml);
        Assert.DoesNotContain("modifiedon", dbml);
        Assert.DoesNotContain("contoso_kind", dbml);
        Assert.Equal(
            [
                new DroppedColumn("contoso_order", "contoso_kind", DropReason.NoReferenceFound),
                new DroppedColumn("contoso_order", "modifiedon", DropReason.NoReferenceFound),
                new DroppedColumn("contoso_order", "owningname", DropReason.PlatformPlumbing),
            ],
            dropped);
    }

    [Fact]
    public void ConvertModel_AtFull_KeepsEveryColumn_AndReturnsNothing()
    {
        using var tree = new TestTree();
        var model = tree.OrderApp();
        var output = Path.Combine(tree.Output, "solution.dbml");

        var dropped = DataModelConverterService.ConvertModel([model], "dbml", output, "contoso_app");

        var dbml = File.ReadAllText(output);
        Assert.Contains("  modifiedon ", dbml);
        Assert.Contains("contoso_kind", dbml);
        Assert.Empty(dropped);
    }

    [Theory]
    [InlineData("contoso", false)]
    [InlineData(null, true)]
    public void ConvertModel_ReadsThePublisherPrefixFromTheManifest(string? prefix, bool platformColumnSurvives)
    {
        using var tree = new TestTree();
        var model = tree.OrderApp(prefix);
        var output = Path.Combine(tree.Output, "solution.dbml");

        DataModelConverterService.ConvertModel([model], "dbml", output, "contoso_app", detail: DetailLevel.Minimal);

        Assert.Equal(platformColumnSurvives, File.ReadAllText(output).Contains("  modifiedon "));
    }

    [Fact]
    public void ConvertModel_AtMinimal_GivesTheSameBytesOnASecondRun()
    {
        using var tree = new TestTree();
        var model = tree.OrderApp();
        var first = Path.Combine(tree.Output, "first.dbml");
        var second = Path.Combine(tree.Output, "second.dbml");

        DataModelConverterService.ConvertModel([model], "dbml", first, "contoso_app", detail: DetailLevel.Minimal);
        DataModelConverterService.ConvertModel([model], "dbml", second, "contoso_app", detail: DetailLevel.Minimal);

        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
    }

    [Fact]
    public void ConvertModel_RejectsMinimalWithoutAnApp_AndWritesNothing()
    {
        using var tree = new TestTree();
        var model = tree.OrderApp();
        var output = Path.Combine(tree.Output, "solution.dbml");

        var ex = Assert.Throws<ArgumentException>(() =>
            DataModelConverterService.ConvertModel([model], "dbml", output, detail: DetailLevel.Minimal));

        Assert.Contains("--app", ex.Message);
        Assert.False(File.Exists(output));
    }

    [Theory]
    [InlineData("sql")]
    [InlineData("edmx")]
    [InlineData("ribbon")]
    public void ConvertModel_RejectsMinimalForATargetOtherThanDbml_AndWritesNothing(string target)
    {
        using var tree = new TestTree();
        var model = tree.OrderApp();
        var output = Path.Combine(tree.Output, "solution." + target);

        var ex = Assert.Throws<ArgumentException>(() =>
            DataModelConverterService.ConvertModel([model], target, output, "contoso_app", detail: DetailLevel.Minimal));

        Assert.Contains("--target dbml", ex.Message);
        Assert.False(File.Exists(output));
    }

    [Theory]
    [InlineData("sql")]
    [InlineData("edmx")]
    [InlineData("ribbon")]
    public void ConvertModel_AtFull_StillConvertsToEveryTarget(string target)
    {
        using var tree = new TestTree();
        var model = tree.OrderApp();
        var output = Path.Combine(tree.Output, "solution." + target);

        DataModelConverterService.ConvertModel([model], target, output, "contoso_app");

        Assert.True(File.Exists(output));
    }
}
