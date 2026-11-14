using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TALXIS.CLI.Core;
using TALXIS.CLI.Features.Data;
using TALXIS.CLI.Features.Data.DataModelConverter;
using TALXIS.CLI.MCP;
using Xunit;

namespace TALXIS.CLI.Tests.Data.DataModelConverter;

[Collection("TxcServicesSerial")]
public class RootDiscoveryTests
{
    private static async Task<int> Run(DataModelConvertCliCommand command)
    {
        using (OutputWriter.RedirectTo(new StringWriter()))
        {
            return await command.RunAsync();
        }
    }

    private static int Occurrences(string text, string fragment) =>
        text.Split(fragment).Length - 1;

    [Fact]
    public void ARoot_YieldsEveryDeclarationsFolderBeneathIt()
    {
        using var tree = new TestTree();
        tree.Declarations("Contoso/Sales/Model/Declarations", "contoso_sales", "contoso_deal");
        tree.Declarations("Contoso/Legacy/CDS", "contoso_legacy", "contoso_old");
        tree.Declarations("Apps/Declarations", null, "contoso_app");

        var folders = DataModelConverterService.DiscoverDeclarationFolders(tree.Root);

        Assert.Equal(
            new[] { "Apps/Declarations", "Contoso/Legacy/CDS", "Contoso/Sales/Model/Declarations" }.Select(tree.Full),
            folders);
    }

    [Fact]
    public void ARootThatIsItselfADeclarationsFolder_IsReturnedAsIs()
    {
        using var tree = new TestTree();
        var declarations = tree.Declarations("Declarations", null, "contoso_thing");

        Assert.Equal([declarations], DataModelConverterService.DiscoverDeclarationFolders(declarations));
    }

    [Theory]
    [InlineData("node_modules")]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData(".git")]
    public void ThrowawayDirectories_AreNotSearched(string throwaway)
    {
        using var tree = new TestTree();
        var real = tree.Declarations("Model/Declarations", "contoso_model", "contoso_thing");
        tree.Declarations($"Model/{throwaway}/Copy/Declarations", "contoso_copy", "contoso_thing");
        tree.Declarations($"{throwaway}/Declarations", null, "contoso_thing");

        Assert.Equal([real], DataModelConverterService.DiscoverDeclarationFolders(tree.Root));
    }

    [Fact]
    public void FoldersAreOrderedByRelativePath_WhateverTheSeparator()
    {
        using var tree = new TestTree();
        string[] names = ["contoso_task", "contoso_task2", "contoso_taskItem", "contoso_taskx"];
        foreach (var name in Enumerable.Reverse(names))
        {
            tree.Declarations($"{name}/Declarations", null, "contoso_thing");
        }

        var folders = DataModelConverterService.DiscoverDeclarationFolders(tree.Root);

        Assert.Equal(names.Select(name => tree.Full($"{name}/Declarations")), folders);
    }

    [Fact]
    public void AnEntityFileTooShallowToBeInADeclarationsFolder_IsNotReportedAsOneAboveTheRoot()
    {
        using var tree = new TestTree();
        tree.Write("Entity.xml", TestTree.Entity("contoso_thing"));
        tree.Write("Model/Entity.xml", TestTree.Entity("contoso_thing"));

        Assert.Empty(DataModelConverterService.DiscoverDeclarationFolders(tree.Root));
    }

    [Fact]
    public void AnEntityFileOutsideAnEntitiesFolder_DoesNotMakeAFolderADeclarationsFolder()
    {
        using var tree = new TestTree();
        var real = tree.Declarations("Model/Declarations", "contoso_model", "contoso_thing");
        tree.Write("Samples/contoso_sample/Entity.xml", TestTree.Entity("contoso_sample"));
        tree.Write("Model/Notes/Drafts/contoso_draft/Entity.xml", TestTree.Entity("contoso_draft"));

        Assert.Equal([real], DataModelConverterService.DiscoverDeclarationFolders(tree.Root));
    }

    [Theory]
    [InlineData("Entities")]
    [InlineData("entities")]
    [InlineData("ENTITIES")]
    public void TheEntitiesFolder_IsMatchedIgnoringCase(string entities)
    {
        using var tree = new TestTree();
        tree.Write($"Model/Declarations/{entities}/contoso_thing/Entity.xml", TestTree.Entity("contoso_thing"));

        Assert.Equal([tree.Full("Model/Declarations")], DataModelConverterService.DiscoverDeclarationFolders(tree.Root));
    }

    [Fact]
    public void ALinkBackToAnAncestor_IsNotFollowed()
    {
        using var tree = new TestTree();
        var real = tree.Declarations("Model/Declarations", "contoso_model", "contoso_thing");
        if (!tree.TryLink("Model/Loop", tree.Root))
        {
            return;
        }

        Assert.Equal([real], DataModelConverterService.DiscoverDeclarationFolders(tree.Root));
    }

    [Fact]
    public void ALinkToAFolderOutsideTheRoot_IsFollowed()
    {
        using var tree = new TestTree();
        tree.Declarations("Outside/Declarations", "contoso_outside", "contoso_thing");
        Directory.CreateDirectory(tree.Full("Root"));
        if (!tree.TryLink("Root/Linked", tree.Full("Outside")))
        {
            return;
        }

        Assert.Equal([tree.Full("Root/Linked/Declarations")], DataModelConverterService.DiscoverDeclarationFolders(tree.Full("Root")));
    }

    [Fact]
    public void ALinkWhoseTargetIsGone_IsSkipped()
    {
        using var tree = new TestTree();
        var real = tree.Declarations("Model/Declarations", "contoso_model", "contoso_thing");
        Directory.CreateDirectory(tree.Full("Gone"));
        if (!tree.TryLink("Model/Broken", tree.Full("Gone")))
        {
            return;
        }

        Directory.Delete(tree.Full("Gone"));

        Assert.Equal([real], DataModelConverterService.DiscoverDeclarationFolders(tree.Root));
    }

    [Fact]
    public void ARootThatDoesNotExist_IsAValidationError()
    {
        using var tree = new TestTree();

        Assert.Throws<ArgumentException>(() => DataModelConverterService.DiscoverDeclarationFolders(tree.Full("missing")));
    }

    [Fact]
    public async Task TheCommand_ConvertsEveryDeclarationsFolderUnderARoot_ButNotBuildOutput()
    {
        using var tree = new TestTree();
        tree.Declarations("Contoso/Sales/Declarations", "contoso_sales", "contoso_deal");
        tree.Declarations("Contoso/Support/CDS", "contoso_support", "contoso_case");
        tree.Declarations("Contoso/Sales/node_modules/pkg/Declarations", null, "contoso_stray");
        tree.Declarations("Contoso/Sales/bin/Debug/Declarations", null, "contoso_stray");

        var exit = await Run(new DataModelConvertCliCommand
        {
            Roots = [tree.Root],
            TargetFormat = "dbml",
            OutputDirectory = tree.Output,
        });

        Assert.Equal(0, exit);
        var dbml = tree.Dbml();
        Assert.Contains("table contoso_deal ", dbml);
        Assert.Contains("table contoso_case ", dbml);
        Assert.DoesNotContain("contoso_stray", dbml);
        Assert.Contains("//contoso_sales", dbml);
        Assert.Contains("//contoso_support", dbml);
    }

    [Fact]
    public async Task TheCommand_MergesARootWithTheInputsItIsGiven()
    {
        using var tree = new TestTree();
        var input = tree.Declarations("Standalone/Declarations", "contoso_standalone", "contoso_solo");
        tree.Declarations("Repo/Model/Declarations", "contoso_model", "contoso_thing");

        var exit = await Run(new DataModelConvertCliCommand
        {
            InputPaths = [input],
            Roots = [tree.Full("Repo")],
            TargetFormat = "dbml",
            OutputDirectory = tree.Output,
        });

        Assert.Equal(0, exit);
        var dbml = tree.Dbml();
        Assert.Contains("table contoso_solo ", dbml);
        Assert.Contains("table contoso_thing ", dbml);
    }

    [Fact]
    public async Task TheCommand_RejectsARootWithNoDeclarations_InsteadOfConvertingTheCurrentDirectory()
    {
        using var tree = new TestTree();
        Directory.CreateDirectory(tree.Full("Empty"));

        var exit = await Run(new DataModelConvertCliCommand
        {
            Roots = [tree.Full("Empty")],
            TargetFormat = "dbml",
            OutputDirectory = tree.Output,
        });

        Assert.Equal(2, exit);
        Assert.False(File.Exists(Path.Combine(tree.Output, "solution.dbml")));
    }

    [Fact]
    public async Task TheCommand_RejectsAnEmptyRoot_EvenWhenAnInputIsGiven()
    {
        using var tree = new TestTree();
        var input = tree.Declarations("Standalone/Declarations", "contoso_standalone", "contoso_solo");
        Directory.CreateDirectory(tree.Full("Empty"));

        var exit = await Run(new DataModelConvertCliCommand
        {
            InputPaths = [input],
            Roots = [tree.Full("Empty")],
            TargetFormat = "dbml",
            OutputDirectory = tree.Output,
        });

        Assert.Equal(2, exit);
        Assert.False(File.Exists(Path.Combine(tree.Output, "solution.dbml")));
    }

    [Fact]
    public async Task TheCommand_RejectsARootThatDoesNotExist()
    {
        using var tree = new TestTree();

        var exit = await Run(new DataModelConvertCliCommand
        {
            Roots = [tree.Full("missing")],
            TargetFormat = "dbml",
            OutputDirectory = tree.Output,
        });

        Assert.Equal(2, exit);
        Assert.False(File.Exists(Path.Combine(tree.Output, "solution.dbml")));
    }

    [Fact]
    public async Task TheCommand_ConvertsTheCurrentDirectory_OnlyWhenGivenNeitherInputNorRoot()
    {
        using var tree = new TestTree();
        tree.Declarations("Here/Declarations", "contoso_here", "contoso_thing");
        var previous = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(tree.Full("Here"));
            var exit = await Run(new DataModelConvertCliCommand
            {
                TargetFormat = "dbml",
                OutputDirectory = tree.Output,
            });

            Assert.Equal(0, exit);
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }

        Assert.Equal(1, Occurrences(tree.Dbml(), "table contoso_thing "));
    }

    [Fact]
    public void TheMcpToolDeclaresRootAsAnArray_AndRepeatsTheFlagPerPath()
    {
        var adapter = new CliCommandAdapter();

        var root = adapter.BuildInputSchema(typeof(DataModelConvertCliCommand)).GetProperty("properties").GetProperty("root");
        var cliArgs = adapter.BuildCliArgs("data_model_convert", new Dictionary<string, JsonElement>
        {
            ["root"] = JsonSerializer.SerializeToElement(new[] { "first", "second" }),
            ["target"] = JsonSerializer.SerializeToElement("dbml"),
        });

        Assert.Equal("array", root.GetProperty("type").GetString());
        Assert.Equal(["data", "model", "convert", "--root", "first", "--root", "second", "--target", "dbml"], cliArgs);
    }
}
