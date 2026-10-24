using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using TALXIS.CLI.Features.Data.DataModelConverter;
using Model = TALXIS.CLI.Features.Data.DataModelConverter.Model;
using Xunit;

namespace TALXIS.CLI.Tests.Data.DataModelConverter;

public class ColumnMetadataTests
{
    private static string Lookup(string name, string? isLogical = null) =>
        $"""<attribute PhysicalName="{name}"><Type>lookup</Type>{(isLogical == null ? "" : $"<IsLogical>{isLogical}</IsLogical>")}</attribute>""";

    private static Model.Module ModuleOf(params string[] attributes)
    {
        var module = new Model.Module { ModuleName = "test" };
        module.entities.Add(XElement.Parse(TestTree.Entity("contoso_thing", attributes)));
        return module;
    }

    private static Model.TableRow Column(Model.ParsedModel model, string name) =>
        model.tables.Single().Rows.Single(row => row.Name == name);

    private static string SolutionZip(string uniqueName, string? prefix)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "customizations.xml", $"<ImportExportXml><Entities>{TestTree.Entity("contoso_thing")}</Entities></ImportExportXml>");
            Add(archive, "solution.xml", $"<ImportExportXml><SolutionManifest><UniqueName>{uniqueName}</UniqueName><Publisher><CustomizationPrefix>{prefix}</CustomizationPrefix></Publisher></SolutionManifest></ImportExportXml>");
        }

        return Convert.ToBase64String(stream.ToArray());
    }

    private static void Add(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open());
        writer.Write(content);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    public void AColumnsLogicalFlagIsRead(string declared, bool expected)
    {
        var model = DataModelConverterService.ParseModules([ModuleOf(Lookup("contoso_owner", declared))]);

        Assert.Equal(expected, Column(model, "contoso_owner").IsLogical);
    }

    [Fact]
    public void AColumnWithoutALogicalFlag_HasNone()
    {
        var model = DataModelConverterService.ParseModules([ModuleOf(Lookup("contoso_owner"))]);

        Assert.Null(Column(model, "contoso_owner").IsLogical);
    }

    [Fact]
    public void ALaterDeclarationSuppliesTheLogicalFlagAnEarlierOneLeftOut()
    {
        var model = DataModelConverterService.ParseModules([ModuleOf(Lookup("contoso_owner")), ModuleOf(Lookup("contoso_owner", "1"))]);

        Assert.True(Column(model, "contoso_owner").IsLogical);
    }

    [Fact]
    public void ALaterDeclarationDoesNotOverwriteALogicalFlagAnEarlierOneGave()
    {
        var model = DataModelConverterService.ParseModules([ModuleOf(Lookup("contoso_owner", "0")), ModuleOf(Lookup("contoso_owner", "1"))]);

        Assert.False(Column(model, "contoso_owner").IsLogical);
    }

    [Fact]
    public void AFolderModuleCarriesThePublisherPrefixOfItsManifest()
    {
        using var tree = new TestTree();
        tree.Declarations("Model/Declarations", null, "contoso_thing");
        tree.Solution("Model/Declarations", "contoso_core", "contoso");

        var model = DataModelConverterService.ParseModelFolder(tree.Full("Model/Declarations"));

        Assert.Equal("contoso", model.tables.Single().ParentModule.CustomizationPrefix);
        Assert.Equal("contoso_core", model.tables.Single().ParentModule.ModuleName);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(true, "")]
    public void AFolderModuleWithoutAReadablePrefix_HasNone(bool withManifest, string? prefix)
    {
        using var tree = new TestTree();
        tree.Declarations("Model/Declarations", null, "contoso_thing");
        if (withManifest)
        {
            tree.Solution("Model/Declarations", "contoso_core", prefix);
        }

        var model = DataModelConverterService.ParseModelFolder(tree.Full("Model/Declarations"));

        Assert.Null(model.tables.Single().ParentModule.CustomizationPrefix);
    }

    [Fact]
    public void AZipModuleCarriesThePublisherPrefixOfItsManifest()
    {
        var model = DataModelConverterService.ParseModel(SolutionZip("contoso_core", "contoso"));

        Assert.Equal("contoso", model.tables.Single().ParentModule.CustomizationPrefix);
    }

    [Fact]
    public void AZipModuleWithAnEmptyPrefix_HasNone()
    {
        var model = DataModelConverterService.ParseModel(SolutionZip("contoso_core", null));

        Assert.Null(model.tables.Single().ParentModule.CustomizationPrefix);
    }
}
