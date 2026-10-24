using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using TALXIS.CLI.Features.Data;
using TALXIS.CLI.Features.Data.DataModelConverter;
using TALXIS.CLI.MCP;
using Model = TALXIS.CLI.Features.Data.DataModelConverter.Model;
using Xunit;

namespace TALXIS.CLI.Tests.Data.DataModelConverter;

public class MultipleInputMergeTests
{
    private static XElement Entity(string logicalName, params string[] attributes) =>
        XElement.Parse($"""
            <Entity>
              <Name LocalizedName="{logicalName}" OriginalName="{logicalName}">{logicalName}</Name>
              <EntityInfo>
                <entity Name="{logicalName}">
                  <attributes>
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

    private static XElement OptionSet(string name, params (int Value, string Label)[] options) =>
        new("optionset",
            new XAttribute("Name", name),
            new XElement("OptionSetType", "picklist"),
            new XElement("options", options.Select(o =>
                new XElement("option",
                    new XAttribute("value", o.Value),
                    new XElement("labels",
                        new XElement("label", new XAttribute("description", o.Label), new XAttribute("languagecode", "1033")))))));

    private static Model.Module WithOptionSets(Model.Module module, params XElement[] optionSets)
    {
        module.optionsets.AddRange(optionSets);
        return module;
    }

    private static Model.Module WithRelationships(Model.Module module, params XElement[] relationships)
    {
        module.relationships.AddRange(relationships);
        return module;
    }

    private static XElement ManyToMany(string name, string first, string second) =>
        XElement.Parse($"""
            <EntityRelationship Name="{name}">
              <EntityRelationshipType>ManyToMany</EntityRelationshipType>
              <FirstEntityName>{first}</FirstEntityName>
              <SecondEntityName>{second}</SecondEntityName>
              <IntersectEntityName>{name}</IntersectEntityName>
            </EntityRelationship>
            """);

    private static XElement WithRibbon(XElement entity, params string[] actionIds)
    {
        var actions = string.Join("", actionIds.Select(id => $"""<CustomAction Id="{id}" Location="contoso.Location" Sequence="10" />"""));
        entity.Add(XElement.Parse($"""
            <RibbonDiffXml>
              <CustomActions>{actions}</CustomActions>
              <CommandDefinitions><CommandDefinition Id="contoso.Command" /></CommandDefinitions>
              <RuleDefinitions><EnableRules><EnableRule Id="contoso.Rule" /></EnableRules></RuleDefinitions>
              <LocLabels><LocLabel Id="contoso.Label"><Titles><Title description="Open" languagecode="1033" /></Titles></LocLabel></LocLabels>
            </RibbonDiffXml>
            """));
        return entity;
    }

    private static int Occurrences(string text, string fragment) =>
        text.Split(fragment).Length - 1;

    private sealed class TempDir : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "txc-merge-" + Path.GetRandomFileName());
        public string Output { get; } = Path.Combine(Path.GetTempPath(), "txc-merge-out-" + Path.GetRandomFileName());

        public TempDir()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Output);
        }

        public string Declarations(string folder, string? solutionName, params XElement[] entities)
        {
            var dir = Path.Combine(Root, folder);
            Directory.CreateDirectory(dir);

            foreach (var entity in entities)
            {
                var entityDir = Path.Combine(dir, "Entities", entity.Element("Name")!.Value);
                Directory.CreateDirectory(entityDir);
                File.WriteAllText(Path.Combine(entityDir, "Entity.xml"), entity.ToString());
            }

            if (solutionName != null)
            {
                Directory.CreateDirectory(Path.Combine(dir, "Other"));
                File.WriteAllText(
                    Path.Combine(dir, "Other", "Solution.xml"),
                    $"<ImportExportXml><SolutionManifest><UniqueName>{solutionName}</UniqueName></SolutionManifest></ImportExportXml>");
            }

            return dir;
        }

        public string Relationships(string declarationsFolder, params XElement[] relationships)
        {
            var dir = Path.Combine(declarationsFolder, "Other", "Relationships");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "links.xml"), new XElement("EntityRelationships", relationships).ToString());
            return declarationsFolder;
        }

        public string Zip(string solutionName, XElement[] entities, params XElement[] relationships)
        {
            var path = Path.Combine(Root, solutionName + ".zip");
            using var stream = File.Create(path);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

            WriteEntry(
                archive,
                "customizations.xml",
                new XElement("ImportExportXml", new XElement("Entities", entities), new XElement("EntityRelationships", relationships)).ToString());
            WriteEntry(archive, "solution.xml", $"<ImportExportXml><SolutionManifest><UniqueName>{solutionName}</UniqueName></SolutionManifest></ImportExportXml>");

            return path;
        }

        public string Run(string format, params string[] inputs)
        {
            var output = Path.Combine(Output, "solution." + format);
            DataModelConverterService.ConvertModel([.. inputs], format, output);
            return File.ReadAllText(output);
        }

        private static void WriteEntry(ZipArchive archive, string name, string content)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write(content);
        }

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
            Directory.Delete(Output, recursive: true);
        }
    }

    private static string Dbml(string declarationsFolder) =>
        DataModelConverterService.ConvertToDBML(DataModelConverterService.ParseModelFolder(declarationsFolder));

    [Fact]
    public void TablesNamedDifferentlyOnlyInCase_MergeIntoOneTable()
    {
        var model = DataModelConverterService.ParseModules(
        [
            ModuleOf("base", Entity("Contoso_Thing", Attr("contoso_a", "nvarchar", 50))),
            ModuleOf("layer", Entity("contoso_thing", Attr("contoso_b", "nvarchar", 50))),
        ]);

        var table = Assert.Single(model.tables);
        var names = table.Rows.Select(r => r.Name).ToList();
        Assert.Contains("contoso_a", names);
        Assert.Contains("contoso_b", names);
    }

    [Theory]
    [InlineData("Contoso_Thing", "contoso_thing")]
    [InlineData("contoso_thing", "Contoso_Thing")]
    public void TablesNamedDifferentlyOnlyInCase_KeepTheFirstSpelling(string first, string second)
    {
        var model = DataModelConverterService.ParseModules(
        [
            ModuleOf("base", Entity(first, Attr("contoso_a", "nvarchar", 50))),
            ModuleOf("layer", Entity(second, Attr("contoso_b", "nvarchar", 50))),
        ]);

        Assert.Equal(first, Assert.Single(model.tables).LogicalName);
    }

    [Fact]
    public void ModuleWithoutAKey_FollowedByOneThatSuppliesIt_YieldsASinglePrimaryKey()
    {
        var model = DataModelConverterService.ParseModules(
        [
            ModuleOf("apps", Entity("contoso_thing", Attr("contoso_a", "nvarchar", 50))),
            ModuleOf("model", Entity("contoso_thing", Attr("activityid", "primarykey"))),
        ]);

        var keys = Assert.Single(model.tables).Rows.Where(r => r.RowType == Model.RowType.Primarykey).ToList();
        Assert.Equal("activityid", Assert.Single(keys).Name);
    }

    [Fact]
    public void TableNoModuleGivesAKey_GetsOneNamedAfterIt()
    {
        var model = DataModelConverterService.ParseModules(
            [ModuleOf("only", Entity("contoso_thing", Attr("contoso_a", "nvarchar", 50)))]);

        var key = Assert.Single(Assert.Single(model.tables).Rows, r => r.RowType == Model.RowType.Primarykey);
        Assert.Equal("contoso_thingid", key.Name);
    }

    [Fact]
    public void TableIsCreditedToTheModuleDeclaringMostOfItsAttributes()
    {
        var model = DataModelConverterService.ParseModules(
        [
            ModuleOf("apps", Entity("contoso_thing")),
            ModuleOf("extension", Entity("contoso_thing", Attr("contoso_x", "int"))),
            ModuleOf("model", Entity("contoso_thing", Attr("contoso_a", "int"), Attr("contoso_b", "int"), Attr("contoso_c", "int"))),
        ]);

        Assert.Equal("model", Assert.Single(model.tables).ParentModule.ModuleName);
        Assert.Contains("//model", DataModelConverterService.ConvertToDBML(model));
    }

    [Fact]
    public void ModulesDeclaringEquallyManyAttributes_CreditTheEarlierOne()
    {
        var model = DataModelConverterService.ParseModules(
        [
            ModuleOf("first", Entity("contoso_thing", Attr("contoso_a", "int"))),
            ModuleOf("second", Entity("contoso_thing", Attr("contoso_b", "int"))),
        ]);

        Assert.Equal("first", Assert.Single(model.tables).ParentModule.ModuleName);
    }

    [Fact]
    public void TableNoModuleGivesAttributes_IsCreditedToTheFirstModuleDeclaringIt()
    {
        var model = DataModelConverterService.ParseModules(
        [
            ModuleOf("first", Entity("contoso_thing")),
            ModuleOf("second", Entity("contoso_thing")),
        ]);

        Assert.Equal("first", Assert.Single(model.tables).ParentModule.ModuleName);
    }

    [Theory]
    [InlineData("One", "Uno", "One")]
    [InlineData("Uno", "One", "Uno")]
    public void OptionSetLabelsDisagreeing_KeepTheFirstModulesLabel(string first, string second, string expected)
    {
        var model = DataModelConverterService.ParseModules(
        [
            WithOptionSets(ModuleOf("first"), OptionSet("contoso_status", (1, first))),
            WithOptionSets(ModuleOf("second"), OptionSet("contoso_status", (1, second), (2, "Two"))),
        ]);

        var optionSet = Assert.Single(model.optionSets);
        Assert.Equal(expected, optionSet.Values.Single(v => v.Value == 1).Label);
        Assert.Contains(optionSet.Values, v => v.Value == 2);
    }

    [Fact]
    public void ManyToManyDeclaredByTwoModules_YieldsOneIntersectTable()
    {
        var link = ManyToMany("contoso_a_b", "contoso_a", "contoso_b");

        var model = DataModelConverterService.ParseModules(
        [
            WithRelationships(ModuleOf("base", Entity("contoso_a"), Entity("contoso_b")), link),
            WithRelationships(ModuleOf("layer", Entity("contoso_a"), Entity("contoso_b")), link),
        ]);

        Assert.Single(model.tables, t => t.LogicalName == "contoso_a_b");
        Assert.Equal(2, model.relationships.Count(r => r.RighSideTable.LogicalName == "contoso_a_b"));
    }

    [Fact]
    public void RibbonItemsDeclaredByTwoModules_AreEmittedOnce()
    {
        var model = DataModelConverterService.ParseModules(
        [
            ModuleOf("base", WithRibbon(Entity("contoso_thing"), "contoso.Action")),
            ModuleOf("layer", WithRibbon(Entity("contoso_thing"), "contoso.Action", "contoso.Extra")),
        ]);

        var ribbon = DataModelConverterService.ConvertToRibbonDiff(model);

        Assert.Equal(1, Occurrences(ribbon, "<CustomAction Id=\"contoso.Action\""));
        Assert.Equal(1, Occurrences(ribbon, "<CustomAction Id=\"contoso.Extra\""));
        Assert.Equal(1, Occurrences(ribbon, "<CommandDefinition Id=\"contoso.Command\""));
        Assert.Equal(1, Occurrences(ribbon, "<EnableRule Id=\"contoso.Rule\""));
        Assert.Equal(1, Occurrences(ribbon, "<LocLabel Id=\"contoso.Label\""));
    }

    [Fact]
    public void TheSameRibbonActionIdOnDifferentTables_IsKeptForBoth()
    {
        var model = DataModelConverterService.ParseModules(
        [
            ModuleOf("base", WithRibbon(Entity("contoso_a"), "contoso.Shared")),
            ModuleOf("layer", WithRibbon(Entity("contoso_b"), "contoso.Shared")),
        ]);

        var ribbon = DataModelConverterService.ConvertToRibbonDiff(model);

        Assert.Equal(2, Occurrences(ribbon, "<CustomAction Id=\"contoso.Shared\""));
    }

    [Fact]
    public void FolderModule_IsNamedAfterItsSolutionUniqueName()
    {
        using var temp = new TempDir();
        var folder = temp.Declarations("anything", "contoso_base", Entity("contoso_thing", Attr("contoso_a", "int")));

        var model = DataModelConverterService.ParseModelFolder(folder);

        Assert.Equal("contoso_base", Assert.Single(model.tables).ParentModule.ModuleName);
        Assert.Contains("//contoso_base", DataModelConverterService.ConvertToDBML(model));
    }

    [Fact]
    public void FolderWithoutASolutionManifest_HasNoModuleName()
    {
        using var temp = new TempDir();
        var folder = temp.Declarations("anything", null, Entity("contoso_thing", Attr("contoso_a", "int")));

        var model = DataModelConverterService.ParseModelFolder(folder);

        Assert.Equal("", Assert.Single(model.tables).ParentModule.ModuleName);
    }

    [Theory]
    [InlineData("contoso_base")]
    [InlineData(null)]
    public void TheSameDeclarationsUnderDifferentParents_ConvertIdentically(string? solutionName)
    {
        using var temp = new TempDir();
        var entity = Entity("contoso_thing", Attr("contoso_a", "int"));
        var first = temp.Declarations("clone-one/Model", solutionName, entity);
        var second = temp.Declarations("elsewhere/deeper/Model", solutionName, entity);

        var dbml = Dbml(first);

        Assert.Equal(dbml, Dbml(second));
        Assert.DoesNotContain("clone-one", dbml);
        Assert.DoesNotContain(Path.GetFileName(temp.Root), dbml);
    }

    [Fact]
    public void SeveralFolders_AreMergedIntoOneModel()
    {
        using var temp = new TempDir();
        var baseDir = temp.Declarations("base", "contoso_base", Entity("contoso_thing", Attr("contoso_a", "int")));
        var layerDir = temp.Declarations("layer", "contoso_layer",
            Entity("contoso_thing", Attr("contoso_b", "int")),
            Entity("contoso_extra", Attr("contoso_c", "int")));

        var dbml = temp.Run("dbml", baseDir, layerDir);

        Assert.Equal(1, Occurrences(dbml, "table contoso_thing "));
        Assert.Equal(1, Occurrences(dbml, "  contoso_a "));
        Assert.Equal(1, Occurrences(dbml, "  contoso_b "));
        Assert.Equal(1, Occurrences(dbml, "table contoso_extra "));
        Assert.Contains("//contoso_base", dbml);
        Assert.Contains("//contoso_layer", dbml);
    }

    [Theory]
    [InlineData("int", "nvarchar", "Int")]
    [InlineData("nvarchar", "int", "Nvarchar")]
    public void WhenFoldersDisagreeOnAColumnType_TheEarlierInputWins(string first, string second, string expected)
    {
        using var temp = new TempDir();
        var firstDir = temp.Declarations("first", "contoso_first", Entity("contoso_thing", Attr("contoso_a", first)));
        var secondDir = temp.Declarations("second", "contoso_second", Entity("contoso_thing", Attr("contoso_a", second)));

        var dbml = temp.Run("dbml", firstDir, secondDir);

        Assert.Equal(1, Occurrences(dbml, "  contoso_a "));
        Assert.Contains($"  contoso_a {expected} ", dbml);
    }

    [Theory]
    [InlineData("dbml")]
    [InlineData("sql")]
    [InlineData("ribbon")]
    public void TheSameFolderTwice_ConvertsLikeOnce(string format)
    {
        using var temp = new TempDir();
        var folder = temp.Relationships(
            temp.Declarations("base", "contoso_base",
                WithRibbon(Entity("contoso_a", Attr("contoso_x", "int")), "contoso.Action"),
                Entity("contoso_b", Attr("contoso_y", "int"))),
            ManyToMany("contoso_a_b", "contoso_a", "contoso_b"));

        var once = temp.Run(format, folder);

        Assert.Equal(once, temp.Run(format, folder, folder));
    }

    [Fact]
    public void TheSameZipTwice_ConvertsLikeOnce()
    {
        using var temp = new TempDir();
        var zip = temp.Zip(
            "contoso_zip",
            [Entity("contoso_a", Attr("contoso_x", "int")), Entity("contoso_b", Attr("contoso_y", "int"))],
            ManyToMany("contoso_a_b", "contoso_a", "contoso_b"));

        Assert.Equal(temp.Run("dbml", zip), temp.Run("dbml", zip, zip));
    }

    [Fact]
    public void ProjectFolderAndItsDeclarationsFolder_ConvertLikeTheDeclarationsAlone()
    {
        using var temp = new TempDir();
        var declarations = temp.Relationships(
            temp.Declarations("project/Declarations", "contoso_base",
                Entity("contoso_a", Attr("contoso_x", "int")),
                Entity("contoso_b", Attr("contoso_y", "int"))),
            ManyToMany("contoso_a_b", "contoso_a", "contoso_b"));
        var project = Path.Combine(temp.Root, "project");
        File.WriteAllText(
            Path.Combine(project, "Model.csproj"),
            "<Project><PropertyGroup><SolutionRootPath>Declarations</SolutionRootPath></PropertyGroup></Project>");

        var once = temp.Run("dbml", declarations);

        Assert.Equal(once, temp.Run("dbml", project, declarations));
        Assert.Equal(once, temp.Run("dbml", declarations, project));
    }

    [Theory]
    [InlineData("sql")]
    [InlineData("ribbon")]
    public void AFolderAndItsParent_DoNotRepeatWhatTheyShare(string format)
    {
        using var temp = new TempDir();
        var declarations = temp.Relationships(
            temp.Declarations("repo/Model", "contoso_base",
                WithRibbon(Entity("contoso_a", Attr("contoso_x", "int")), "contoso.Action"),
                Entity("contoso_b", Attr("contoso_y", "int"))),
            ManyToMany("contoso_a_b", "contoso_a", "contoso_b"));
        var repo = Path.Combine(temp.Root, "repo");

        Assert.Equal(temp.Run(format, declarations), temp.Run(format, repo, declarations));
    }

    [Fact]
    public void FolderAndZip_CanBeMixed()
    {
        using var temp = new TempDir();
        var folder = temp.Declarations("base", "contoso_base", Entity("contoso_thing", Attr("contoso_a", "int")));
        var zip = temp.Zip(
            "contoso_zip",
            [Entity("contoso_thing", Attr("contoso_b", "int")), Entity("contoso_other", Attr("contoso_c", "int"))]);

        var dbml = temp.Run("dbml", folder, zip);

        Assert.Equal(1, Occurrences(dbml, "table contoso_thing "));
        Assert.Equal(1, Occurrences(dbml, "  contoso_a "));
        Assert.Equal(1, Occurrences(dbml, "  contoso_b "));
        Assert.Contains("//contoso_base", dbml);
        Assert.Contains("//contoso_zip", dbml);
    }

    [Fact]
    public void SeveralZips_AreMergedThroughTheBase64EntryPoint()
    {
        using var temp = new TempDir();
        var zips = new[]
        {
            temp.Zip("contoso_one", [Entity("contoso_thing", Attr("contoso_a", "int"))]),
            temp.Zip("contoso_two", [Entity("contoso_thing", Attr("contoso_b", "int"))]),
        };

        var model = DataModelConverterService.ParseModel([.. zips.Select(z => Convert.ToBase64String(File.ReadAllBytes(z)))]);

        var table = Assert.Single(model.tables);
        Assert.Equal(["contoso_a", "contoso_b", "contoso_thingid"], table.Rows.Select(r => r.Name).Order().ToArray());
        Assert.Equal("contoso_one", table.ParentModule.ModuleName);
    }

    [Fact]
    public void SeveralFoldersCopiedElsewhere_ConvertIdentically()
    {
        using var temp = new TempDir();
        var entities = new[]
        {
            (Folder: "base", Solution: "contoso_base", Entity: Entity("contoso_thing", Attr("contoso_a", "int"))),
            (Folder: "layer", Solution: "contoso_layer", Entity: Entity("contoso_thing", Attr("contoso_b", "int"))),
        };
        var here = entities.Select(e => temp.Declarations($"clone-one/{e.Folder}", e.Solution, e.Entity)).ToArray();
        var there = entities.Select(e => temp.Declarations($"elsewhere/deeper/{e.Folder}", e.Solution, e.Entity)).ToArray();

        var dbml = temp.Run("dbml", here);

        Assert.Equal(dbml, temp.Run("dbml", there));
        Assert.Equal(dbml, temp.Run("dbml", here));
    }

    [Fact]
    public void AnInputThatDoesNotExist_IsReported()
    {
        using var temp = new TempDir();

        Assert.Throws<FileNotFoundException>(() => temp.Run("dbml", Path.Combine(temp.Root, "missing")));
    }

    [Fact]
    public void NoInputs_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => DataModelConverterService.ConvertModel(new List<string>(), "dbml", "unused"));
    }

    [Fact]
    public async Task TheCommand_MergesEveryInputItIsGiven()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Output, ".gitignore"), "");
        var first = temp.Declarations("first", "contoso_first", Entity("contoso_thing", Attr("contoso_a", "int")));
        var second = temp.Declarations("second", "contoso_second", Entity("contoso_thing", Attr("contoso_b", "int")));
        var command = new DataModelConvertCliCommand
        {
            InputPaths = [first, second],
            TargetFormat = "dbml",
            OutputDirectory = temp.Output,
        };

        var execute = typeof(DataModelConvertCliCommand).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var exitCode = await (Task<int>)execute.Invoke(command, null)!;

        Assert.Equal(0, exitCode);
        var dbml = File.ReadAllText(Path.Combine(temp.Output, "solution.dbml"));
        Assert.Equal(1, Occurrences(dbml, "table contoso_thing "));
        Assert.Contains("  contoso_a ", dbml);
        Assert.Contains("  contoso_b ", dbml);
    }

    [Fact]
    public void TheMcpToolDeclaresInputAsAnArray_AndRepeatsTheFlagPerPath()
    {
        var adapter = new CliCommandAdapter();

        var input = adapter.BuildInputSchema(typeof(DataModelConvertCliCommand)).GetProperty("properties").GetProperty("input");
        var cliArgs = adapter.BuildCliArgs("data_model_convert", new Dictionary<string, JsonElement>
        {
            ["input"] = JsonSerializer.SerializeToElement(new[] { "first", "second" }),
            ["target"] = JsonSerializer.SerializeToElement("dbml"),
        });

        Assert.Equal("array", input.GetProperty("type").GetString());
        Assert.Equal(["data", "model", "convert", "--input", "first", "--input", "second", "--target", "dbml"], cliArgs);
    }

    [Fact]
    public void TheMcpTool_StillAcceptsASingleInputString()
    {
        var cliArgs = new CliCommandAdapter().BuildCliArgs("data_model_convert", new Dictionary<string, JsonElement>
        {
            ["input"] = JsonSerializer.SerializeToElement("only"),
            ["target"] = JsonSerializer.SerializeToElement("dbml"),
        });

        Assert.Equal(["data", "model", "convert", "--input", "only", "--target", "dbml"], cliArgs);
    }
}
