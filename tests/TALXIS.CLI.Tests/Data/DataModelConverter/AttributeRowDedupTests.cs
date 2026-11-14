using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using TALXIS.CLI.Features.Data.DataModelConverter;
using Model = TALXIS.CLI.Features.Data.DataModelConverter.Model;
using Xunit;

namespace TALXIS.CLI.Tests.Data.DataModelConverter;

public class AttributeRowDedupTests
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

    private static string Attr(string name, string type, int? maxLength = null, string? optionSet = null) =>
        $"""<attribute PhysicalName="{name}"><Type>{type}</Type>{(maxLength is null ? "" : $"<MaxLength>{maxLength}</MaxLength>")}{(optionSet is null ? "" : $"<OptionSetName>{optionSet}</OptionSetName>")}</attribute>""";

    private static Model.Module ModuleOf(string name, params XElement[] entities)
    {
        var module = new Model.Module { ModuleName = name };
        module.entities.AddRange(entities);
        return module;
    }

    private static Model.TableRow[] RowsNamed(Model.ParsedModel model, string table, string column) =>
        model.tables.Single(t => t.LogicalName == table).Rows
            .Where(r => string.Equals(r.Name, column, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    [Fact]
    public void SameColumnDeclaredByTwoModules_IsListedOnce()
    {
        var model = DataModelConverterService.ParseModules(
        [
            ModuleOf("base", Entity("contoso_thing", Attr("contoso_shared", "nvarchar", 50))),
            ModuleOf("layer", Entity("contoso_thing", Attr("contoso_shared", "nvarchar", 50))),
        ]);

        Assert.Single(RowsNamed(model, "contoso_thing", "contoso_shared"));
    }

    [Fact]
    public void ARowAlreadyHeldInAnotherCase_IsNotListedAgain()
    {
        var table = new Model.Table { LogicalName = "contoso_thing" };
        table.Rows.Add(new Model.TableRow("Contoso_Shared", Model.RowType.Int));

        table.ParseMultipleRowsFromXml([XElement.Parse(Attr("contoso_shared", "int"))]);

        Assert.Single(table.Rows);
    }

    [Fact]
    public void ColumnsOnlyOneDeclarationHas_AreAllKept()
    {
        var model = DataModelConverterService.ParseModules(
        [
            ModuleOf("base", Entity("contoso_thing", Attr("contoso_a", "nvarchar", 50), Attr("contoso_shared", "int"))),
            ModuleOf("layer", Entity("contoso_thing", Attr("contoso_shared", "int"), Attr("contoso_b", "nvarchar", 50))),
        ]);

        var names = model.tables.Single(t => t.LogicalName == "contoso_thing").Rows.Select(r => r.Name).ToList();

        Assert.Equal(["contoso_thingid", "contoso_a", "contoso_shared", "contoso_b"], names);
    }

    [Theory]
    [InlineData("nvarchar", "int", Model.RowType.Nvarchar)]
    [InlineData("int", "nvarchar", Model.RowType.Int)]
    public void ConflictingTypes_KeepTheFirstDeclaration(string first, string second, Model.RowType expected)
    {
        var model = DataModelConverterService.ParseModules(
        [
            ModuleOf("first", Entity("contoso_thing", Attr("contoso_field", first, 50))),
            ModuleOf("second", Entity("contoso_thing", Attr("contoso_field", second, 50))),
        ]);

        Assert.Equal(expected, Assert.Single(RowsNamed(model, "contoso_thing", "contoso_field")).RowType);
    }

    [Theory]
    [InlineData(50, 200, 200)]
    [InlineData(200, 50, 200)]
    public void DifferingTextLengths_WidenAndNeverNarrow(int first, int second, int expected)
    {
        var model = DataModelConverterService.ParseModules(
        [
            ModuleOf("first", Entity("contoso_thing", Attr("contoso_text", "nvarchar", first))),
            ModuleOf("second", Entity("contoso_thing", Attr("contoso_text", "nvarchar", second))),
        ]);

        Assert.Equal(expected, Assert.Single(RowsNamed(model, "contoso_thing", "contoso_text")).MaxLenght);
    }

    [Fact]
    public void SameTypeNamingDifferentOptionSets_KeepsTheFirstOptionSet()
    {
        var table = new Model.Table { LogicalName = "contoso_thing" };

        table.ParseMultipleRowsFromXml([XElement.Parse(Attr("contoso_kind", "picklist", optionSet: "contoso_first"))]);
        table.ParseMultipleRowsFromXml([XElement.Parse(Attr("contoso_kind", "picklist", optionSet: "contoso_second"))]);

        Assert.Equal("contoso_first", Assert.Single(table.Rows).OptionSetName);
    }

    [Fact]
    public void OneFolderDeclaringATableTwice_ListsEachColumnOnce()
    {
        var dir = Path.Combine(Path.GetTempPath(), "txc-dedup-" + Path.GetRandomFileName());
        try
        {
            foreach (var entityDir in new[] { "Entities/contoso_thing", "Copy/Entities/contoso_thing" })
            {
                Directory.CreateDirectory(Path.Combine(dir, entityDir));
                File.WriteAllText(
                    Path.Combine(dir, entityDir, "Entity.xml"),
                    Entity("contoso_thing", Attr("contoso_field", "nvarchar", 50)).ToString());
            }

            var model = DataModelConverterService.ParseModelFolder(dir);

            Assert.Single(RowsNamed(model, "contoso_thing", "contoso_field"));
            Assert.Single(RowsNamed(model, "contoso_thing", "contoso_thingid"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
