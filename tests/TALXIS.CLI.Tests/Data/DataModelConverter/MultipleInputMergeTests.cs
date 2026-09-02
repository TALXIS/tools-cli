using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using TALXIS.CLI.Features.Data.DataModelConverter;
using Model = TALXIS.CLI.Features.Data.DataModelConverter.Model;
using Xunit;

namespace TALXIS.CLI.Tests.Data.DataModelConverter;

/// <summary>
/// Merging several declaration folders into one model. A delivery project's data model is
/// spread across the modules a product ships plus the project's own layer, and several of
/// them declare part of the same table — so converting each separately and concatenating
/// the output keeps only the first declaration of each table.
/// </summary>
public class MultipleInputMergeTests
{
    private static XElement Entity(string logicalName, params string[] attributes) =>
        XElement.Parse($"""
            <Entity>
              <Name LocalizedName="{logicalName}" OriginalName="{logicalName}">{logicalName}</Name>
              <EntityInfo>
                <entity Name="{logicalName}">
                  <attributes>
                    <attribute PhysicalName="{logicalName}id"><Type>primarykey</Type></attribute>
                    {string.Join("", attributes)}
                  </attributes>
                </entity>
              </EntityInfo>
            </Entity>
            """);

    private static string Attr(string name, string type, int? maxLength = null) =>
        $"""<attribute PhysicalName="{name}"><Type>{type}</Type>{(maxLength is null ? "" : $"<MaxLength>{maxLength}</MaxLength>")}</attribute>""";

    private static Model.Module ModuleOf(string name, params XElement[] entities)
    {
        var module = new Model.Module { ModuleName = name };
        module.entities.AddRange(entities);
        return module;
    }

    private static Model.Table TableIn(Model.ParsedModel model, string logicalName) =>
        model.tables.Single(t => t.LogicalName == logicalName);

    [Fact]
    public void TwoModulesDeclaringOneTable_MergeIntoASingleTableWithBothColumns()
    {
        var a = ModuleOf("base", Entity("contoso_thing", Attr("contoso_fromBase", "nvarchar", 50)));
        var b = ModuleOf("layer", Entity("contoso_thing", Attr("contoso_fromLayer", "nvarchar", 50)));

        var model = DataModelConverterService.ParseModules([a, b]);

        Assert.Single(model.tables, t => t.LogicalName == "contoso_thing");
        var names = TableIn(model, "contoso_thing").Rows.Select(r => r.Name).ToList();
        Assert.Contains("contoso_frombase", names.Select(n => n.ToLowerInvariant()));
        Assert.Contains("contoso_fromlayer", names.Select(n => n.ToLowerInvariant()));
    }

    [Fact]
    public void SameAttributeDeclaredInBothModules_ProducesOneColumnNotTwo()
    {
        var attr = Attr("contoso_shared", "nvarchar", 50);
        var model = DataModelConverterService.ParseModules(
            [ModuleOf("base", Entity("contoso_thing", attr)), ModuleOf("layer", Entity("contoso_thing", attr))]);

        var rows = TableIn(model, "contoso_thing").Rows
            .Where(r => string.Equals(r.Name, "contoso_shared", System.StringComparison.OrdinalIgnoreCase));
        Assert.Single(rows);
    }

    [Fact]
    public void ConflictingTypeForOneAttribute_KeepsTheFirstInputsDeclaration()
    {
        var model = DataModelConverterService.ParseModules(
        [
            ModuleOf("first",  Entity("contoso_thing", Attr("contoso_field", "nvarchar", 50))),
            ModuleOf("second", Entity("contoso_thing", Attr("contoso_field", "int"))),
        ]);

        var row = TableIn(model, "contoso_thing").Rows
            .Single(r => string.Equals(r.Name, "contoso_field", System.StringComparison.OrdinalIgnoreCase));
        Assert.Equal(Model.RowType.Nvarchar, row.RowType);
    }

    [Theory]
    [InlineData(50, 200, 200)]
    [InlineData(200, 50, 200)]
    public void DifferingTextLengths_WidenNeverNarrow_RegardlessOfInputOrder(int first, int second, int expected)
    {
        var model = DataModelConverterService.ParseModules(
        [
            ModuleOf("first",  Entity("contoso_thing", Attr("contoso_text", "nvarchar", first))),
            ModuleOf("second", Entity("contoso_thing", Attr("contoso_text", "nvarchar", second))),
        ]);

        var row = TableIn(model, "contoso_thing").Rows
            .Single(r => string.Equals(r.Name, "contoso_text", System.StringComparison.OrdinalIgnoreCase));
        Assert.Equal(expected, row.MaxLenght);
    }

    [Fact]
    public void ModulesAreColouredApart_SoAMergedDiagramShowsWhereEachTableCameFrom()
    {
        var a = new Model.Module { ModuleName = "src/Modules.Core/Model" };
        var b = new Model.Module { ModuleName = "Areas/Service/Project/Model" };

        // Assigned through an object initializer, which runs after the constructor body —
        // a colour computed in the constructor would be identical for both.
        Assert.NotEqual(a.Colorhex, b.Colorhex);
    }

    [Fact]
    public void OneFolder_ThroughTheListEntryPoint_MatchesTheSingleFolderEntryPoint()
    {
        var dir = Path.Combine(Path.GetTempPath(), "txc-merge-" + Path.GetRandomFileName());
        var entityDir = Path.Combine(dir, "Entities", "contoso_thing");
        Directory.CreateDirectory(entityDir);
        File.WriteAllText(Path.Combine(entityDir, "Entity.xml"),
            Entity("contoso_thing", Attr("contoso_field", "nvarchar", 50)).ToString());
        try
        {
            var single = DataModelConverterService.ParseModelFolder(dir);
            var viaList = DataModelConverterService.ParseModelFolders([dir]);

            Assert.Equal(single.tables.Count, viaList.tables.Count);
            Assert.Equal(single.relationships.Count, viaList.relationships.Count);
            Assert.Equal(
                single.tables.Select(t => t.LogicalName).OrderBy(x => x),
                viaList.tables.Select(t => t.LogicalName).OrderBy(x => x));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TwoFoldersOnDisk_MergeAttributesAcrossTheFolderBoundary()
    {
        var root = Path.Combine(Path.GetTempPath(), "txc-merge-" + Path.GetRandomFileName());
        var folders = new List<string>();
        foreach (var (name, attr) in new[] { ("base", "contoso_a"), ("layer", "contoso_b") })
        {
            var dir = Path.Combine(root, name, "Declarations");
            Directory.CreateDirectory(Path.Combine(dir, "Entities", "contoso_thing"));
            File.WriteAllText(Path.Combine(dir, "Entities", "contoso_thing", "Entity.xml"),
                Entity("contoso_thing", Attr(attr, "nvarchar", 50)).ToString());
            folders.Add(dir);
        }
        try
        {
            var model = DataModelConverterService.ParseModelFolders(folders);
            var names = TableIn(model, "contoso_thing").Rows
                .Select(r => r.Name.ToLowerInvariant()).ToList();

            Assert.Contains("contoso_a", names);
            Assert.Contains("contoso_b", names);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
