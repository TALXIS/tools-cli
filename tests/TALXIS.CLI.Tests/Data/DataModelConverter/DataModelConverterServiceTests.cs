using System.IO;
using System.Linq;
using System.Reflection;
using DotMake.CommandLine;
using TALXIS.CLI.Features.Data;
using System.Xml.Linq;
using TALXIS.CLI.Features.Data.DataModelConverter;
using Model = TALXIS.CLI.Features.Data.DataModelConverter.Model;
using Xunit;

namespace TALXIS.CLI.Tests.Data.DataModelConverter;

/// <summary>
/// Regression tests for four defects that made the converter silently lose or corrupt
/// model content. Each was found by diffing converter output against the source
/// declarations of real solutions; each test fails against the unfixed converter.
/// </summary>
public class DataModelConverterServiceTests
{
    private static XElement Entity(string logicalName, params string[] attributes)
    {
        var attrXml = string.Join("", attributes);
        return XElement.Parse($"""
            <Entity>
              <Name LocalizedName="{logicalName}" OriginalName="{logicalName}">{logicalName}</Name>
              <EntityInfo>
                <entity Name="{logicalName}">
                  <attributes>
                    <attribute PhysicalName="{logicalName}id"><Type>primarykey</Type></attribute>
                    {attrXml}
                  </attributes>
                </entity>
              </EntityInfo>
            </Entity>
            """);
    }

    private static string Attr(string name, string type) =>
        $"""<attribute PhysicalName="{name}"><Type>{type}</Type></attribute>""";

    private static string Lookup(string name) => Attr(name, "lookup");

    private static XElement OneToMany(string name, string child, string childAttr, string parent) =>
        XElement.Parse($"""
            <EntityRelationship Name="{name}">
              <EntityRelationshipType>OneToMany</EntityRelationshipType>
              <ReferencingEntityName>{child}</ReferencingEntityName>
              <ReferencedEntityName>{parent}</ReferencedEntityName>
              <ReferencingAttributeName>{childAttr}</ReferencingAttributeName>
            </EntityRelationship>
            """);

    private static Model.Module ModuleWith(XElement[] entities, XElement[]? relationships = null)
    {
        var module = new Model.Module();
        module.entities.AddRange(entities);
        if (relationships != null) module.relationships.AddRange(relationships);
        return module;
    }

    // ---- Defect 1: relationships deduped on the table pair, not the column -------------

    [Fact]
    public void TwoLookupsBetweenSameTablePair_BothProduceRelationships()
    {
        var module = ModuleWith(
            [Entity("account"), Entity("contoso_project", Lookup("contoso_ownerid"), Lookup("contoso_billtoid"))],
            [
                OneToMany("rel_owner",  "contoso_project", "contoso_ownerid",  "account"),
                OneToMany("rel_billto", "contoso_project", "contoso_billtoid", "account"),
            ]);

        var model = DataModelConverterService.ParseModules([module]);

        var toAccount = model.relationships
            .Where(r => r.RighSideTable?.LogicalName == "account")
            .Select(r => r.LeftSideRow?.Name)
            .ToList();

        Assert.Equal(2, toAccount.Count);
        Assert.Contains("contoso_ownerid", toAccount);
        Assert.Contains("contoso_billtoid", toAccount);
    }

    // ---- Defect 3: a column vanishes when its option set will not resolve --------------

    [Fact]
    public void PicklistWithUnresolvableOptionSet_KeepsColumnInsteadOfDroppingIt()
    {
        var picklist = """
            <attribute PhysicalName="contoso_statuscode">
              <Type>picklist</Type>
              <OptionSetName>contoso_never_declared_anywhere</OptionSetName>
            </attribute>
            """;
        var module = ModuleWith([Entity("contoso_thing", picklist)]);

        var table = DataModelConverterService.ParseModules([module])
            .tables.Single(t => t.LogicalName == "contoso_thing");

        var row = table.Rows.SingleOrDefault(r => r.Name == "contoso_statuscode");
        Assert.NotNull(row);
        // Cleared so the column cannot reference an Enum that was never emitted;
        // RowType is deliberately left alone so each translator keeps its own handling.
        Assert.True(string.IsNullOrEmpty(row!.OptionSetName));
    }

    // ---- Defect 4: output was not reproducible — colours came from new Random() --------

    [Fact]
    public void ModuleColour_IsDerivedFromName_SoConversionIsReproducible()
    {
        var a = new Model.Module("Areas/Service/Project/Model", new XDocument(new XElement("root")));
        var b = new Model.Module("Areas/Service/Project/Model", new XDocument(new XElement("root")));
        var other = new Model.Module("Areas/Environment/Start/Model", new XDocument(new XElement("root")));

        Assert.Equal(a.Colorhex, b.Colorhex);
        Assert.NotEqual(a.Colorhex, other.Colorhex);
        Assert.Matches("^#[0-9A-F]{6}$", a.Colorhex);
    }

    // ---- Defect 5: a self-referencing N:N emitted duplicate columns and refs -----------

    [Fact]
    public void SelfReferencingManyToMany_ProducesTwoDistinctIntersectColumns()
    {
        var manyToMany = XElement.Parse("""
            <EntityRelationship Name="contoso_thing_thing">
              <EntityRelationshipType>ManyToMany</EntityRelationshipType>
              <FirstEntityName>contoso_thing</FirstEntityName>
              <SecondEntityName>contoso_thing</SecondEntityName>
              <IntersectEntityName>contoso_thing_thing</IntersectEntityName>
            </EntityRelationship>
            """);
        var module = ModuleWith([Entity("contoso_thing")], [manyToMany]);

        var model = DataModelConverterService.ParseModules([module]);
        var intersect = model.tables.Single(t => t.LogicalName == "contoso_thing_thing");

        var names = intersect.Rows.Select(r => r.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());

        // Two legs, each anchored on its own column — one shared column produced a
        // duplicate endpoint pair, which a DBML parser rejects outright.
        var legs = model.relationships.Where(r => r.RighSideTable?.LogicalName == "contoso_thing_thing").ToList();
        Assert.Equal(2, legs.Count);
        Assert.Equal(2, legs.Select(l => l.RighSideRow?.Name).Distinct().Count());

        // The legs also need distinct relationship names: EDMX renders the intersect side
        // as NavigationProperty Name="{relationship.Name}", with a matching Partner and
        // NavigationPropertyBinding Path, so sharing one name emits each of them twice.
        Assert.Equal(2, legs.Select(l => l.Name).Distinct().Count());
    }

    [Fact]
    public void SelfReferencingManyToMany_RendersDistinctNavigationPropertiesOnTheIntersect()
    {
        var manyToMany = XElement.Parse("""
            <EntityRelationship Name="contoso_thing_thing">
              <EntityRelationshipType>ManyToMany</EntityRelationshipType>
              <FirstEntityName>contoso_thing</FirstEntityName>
              <SecondEntityName>contoso_thing</SecondEntityName>
              <IntersectEntityName>contoso_thing_thing</IntersectEntityName>
            </EntityRelationship>
            """);
        var module = ModuleWith([Entity("contoso_thing")], [manyToMany]);

        var model = DataModelConverterService.ParseModules([module]);
        var edmx = DataModelConverterService.ConvertToEDMX(model);

        // The intersect's own EntityType carries one navigation property per leg.
        var intersect = System.Text.RegularExpressions.Regex.Match(
            edmx, "<EntityType Name=\"contoso_thing_thing\".*?</EntityType>",
            System.Text.RegularExpressions.RegexOptions.Singleline).Value;
        var navNames = System.Text.RegularExpressions.Regex
            .Matches(intersect, "<NavigationProperty Name=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value).ToList();

        Assert.Equal(2, navNames.Count);
        Assert.Equal(2, navNames.Distinct().Count());
    }

    // ---- Every relationship endpoint must be non-null: the translators dereference -----
    // ---- LeftSideRow/RighSideRow without a null check. -------------------------------

    [Fact]
    public void EveryEmittedRelationship_HasBothEndpointsResolved()
    {
        var module = ModuleWith(
            [Entity("account"), Entity("contoso_project", Lookup("contoso_ownerid"))],
            [OneToMany("rel_owner", "contoso_project", "contoso_ownerid", "account")]);

        var model = DataModelConverterService.ParseModules([module]);

        Assert.All(model.relationships, r =>
        {
            Assert.NotNull(r.LeftSideTable);
            Assert.NotNull(r.LeftSideRow);
            Assert.NotNull(r.RighSideTable);
            Assert.NotNull(r.RighSideRow);
        });
    }

    // ---- Every format the CLI advertises must actually convert ------------------------
    // plainsql was listed in the option's AllowedValues and fully implemented in the
    // format switch, but missing from the service's SupportedFormats guard -- so it was
    // rejected on every invocation, and the error message listed that same guard as truth.

    [Fact]
    public void EveryAdvertisedTargetFormat_IsAcceptedByTheService()
    {
        var advertised = typeof(DataModelConvertCliCommand)
            .GetProperty(nameof(DataModelConvertCliCommand.TargetFormat))!
            .GetCustomAttribute<CliOptionAttribute>()!
            .AllowedValues!
            .Cast<string>()
            .ToList();

        Assert.NotEmpty(advertised);

        var dir = Path.Combine(Path.GetTempPath(), "txc-fmt-" + Path.GetRandomFileName());
        var entityDir = Path.Combine(dir, "Entities", "contoso_thing");
        Directory.CreateDirectory(entityDir);
        File.WriteAllText(Path.Combine(entityDir, "Entity.xml"), Entity("contoso_thing").ToString());
        try
        {
            foreach (var format in advertised)
            {
                var outFile = Path.Combine(dir, "out." + format);
                var ex = Record.Exception(() => DataModelConverterService.ConvertModel(dir, format, outFile));
                Assert.True(ex is null, $"--target {format} is advertised but failed: {ex?.Message}");
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
