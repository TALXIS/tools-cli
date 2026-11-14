using System;
using System.Linq;
using TALXIS.CLI.Features.Data.DataModelConverter.AppScope;
using Xunit;

namespace TALXIS.CLI.Tests.Data.DataModelConverter;

public class AppScopeResolverTests
{
    private static string[] TablesOf(ResolvedAppScope scope) => [.. scope.TableLogicalNames.Order(StringComparer.Ordinal)];

    [Fact]
    public void OnlyEntityComponentsContributeTables()
    {
        using var tree = new TestTree();
        tree.AppModule("Apps/Declarations", "contoso_app", "contoso_app",
            TestTree.Component("1", "contoso_thing"),
            TestTree.Component("26", "contoso_view"),
            TestTree.Component("60", "contoso_form"),
            TestTree.Component("62", "contoso_app"),
            TestTree.Component("1", "contoso_account"));

        var scope = AppScopeResolver.Resolve([tree.Root], "contoso_app");

        Assert.Equal(["contoso_account", "contoso_thing"], TablesOf(scope));
    }

    [Theory]
    [InlineData("Declarations")]
    [InlineData("CDS")]
    public void DeclarationsAreFoundUnderEitherFolderName(string declarations)
    {
        using var tree = new TestTree();
        tree.AppModule($"Apps/{declarations}", "contoso_app", "contoso_app", TestTree.Component("1", "contoso_thing"));

        var scope = AppScopeResolver.Resolve([tree.Root], "contoso_app");

        Assert.Equal(["contoso_thing"], TablesOf(scope));
    }

    [Fact]
    public void OneAppDeclaredAcrossSeveralFiles_UnionsItsComponents()
    {
        using var tree = new TestTree();
        tree.AppModule("Apps/Declarations", "contoso_app", "contoso_app", TestTree.Component("1", "contoso_first"));
        tree.AppModule("Other/Declarations", "contoso_app", "contoso_app", TestTree.Component("1", "contoso_second", "Added"));

        var scope = AppScopeResolver.Resolve([tree.Root], "contoso_app");

        Assert.Equal(["contoso_first", "contoso_second"], TablesOf(scope));
        Assert.Equal(2, scope.SourceFiles.Count);
    }

    [Fact]
    public void IdentityComesFromTheFileContent_NotTheFolderName()
    {
        using var tree = new TestTree();
        tree.AppModule("Apps/Declarations", "contoso_storefront", "contoso_app", TestTree.Component("1", "contoso_thing"));

        Assert.Equal(["contoso_thing"], TablesOf(AppScopeResolver.Resolve([tree.Root], "CONTOSO_APP")));
        Assert.Throws<ArgumentException>(() => AppScopeResolver.Resolve([tree.Root], "contoso_storefront"));
    }

    [Fact]
    public void SiteMapEntities_AreIncluded_FromBothTheAttributeAndTheUrl()
    {
        using var tree = new TestTree();
        tree.AppModule("Apps/Declarations", "contoso_app", "contoso_app", TestTree.Component("1", "contoso_thing"));
        tree.SiteMap("Apps/Declarations", "contoso_app", "contoso_app", """
            <SubArea Id="a" Entity="contoso_viaattribute" />
            <SubArea Id="b" Url="/main.aspx?pagetype=entitylist&amp;etn=contoso_viaurl&amp;viewid={x}" />
            """);

        var scope = AppScopeResolver.Resolve([tree.Root], "contoso_app");

        Assert.Equal(["contoso_thing", "contoso_viaattribute", "contoso_viaurl"], TablesOf(scope));
    }

    [Fact]
    public void ASiteMapOfAnotherApp_IsNotRead()
    {
        using var tree = new TestTree();
        tree.AppModule("Apps/Declarations", "contoso_app", "contoso_app", TestTree.Component("1", "contoso_thing"));
        tree.SiteMap("Apps/Declarations", "contoso_other", "contoso_other", """<SubArea Id="a" Entity="contoso_foreign" />""");

        Assert.Equal(["contoso_thing"], TablesOf(AppScopeResolver.Resolve([tree.Root], "contoso_app")));
    }

    [Theory]
    [InlineData("a", "z")]
    [InlineData("z", "a")]
    public void AComponentAFragmentRemoves_IsDropped_WhicheverFileIsReadFirst(string baseFolder, string fragmentFolder)
    {
        using var tree = new TestTree();
        tree.AppModule($"{baseFolder}/Declarations", "contoso_app", "contoso_app",
            TestTree.Component("1", "contoso_kept"),
            TestTree.Component("1", "contoso_removed"));
        tree.AppModule($"{fragmentFolder}/Declarations", "contoso_app", "contoso_app",
            TestTree.Component("1", "contoso_removed", "Removed"),
            TestTree.Component("1", "contoso_added", "Added"));

        var scope = AppScopeResolver.Resolve([tree.Root], "contoso_app");

        Assert.Equal(["contoso_added", "contoso_kept"], TablesOf(scope));
    }

    [Fact]
    public void AComponentThatIsRemoved_IsDroppedEvenWhenTheSiteMapStillNamesIt()
    {
        using var tree = new TestTree();
        tree.AppModule("Apps/Declarations", "contoso_app", "contoso_app",
            TestTree.Component("1", "contoso_kept"),
            TestTree.Component("1", "contoso_removed", "Removed"));
        tree.SiteMap("Apps/Declarations", "contoso_app", "contoso_app", """<SubArea Id="a" Entity="contoso_removed" />""");

        Assert.Equal(["contoso_kept"], TablesOf(AppScopeResolver.Resolve([tree.Root], "contoso_app")));
    }

    [Theory]
    [InlineData("Added")]
    [InlineData("Modified")]
    [InlineData(null)]
    public void AnyOtherSolutionAction_KeepsTheComponent(string? solutionAction)
    {
        using var tree = new TestTree();
        tree.AppModule("Apps/Declarations", "contoso_app", "contoso_app", TestTree.Component("1", "contoso_thing", solutionAction));

        Assert.Equal(["contoso_thing"], TablesOf(AppScopeResolver.Resolve([tree.Root], "contoso_app")));
    }

    [Fact]
    public void AnUnknownApp_IsAValidationErrorListingTheAppsFound()
    {
        using var tree = new TestTree();
        tree.AppModule("Apps/Declarations", "contoso_sales", "contoso_sales", TestTree.Component("1", "contoso_thing"));
        tree.AppModule("Apps/Declarations", "contoso_support", "contoso_support", TestTree.Component("1", "contoso_case"));

        var ex = Assert.Throws<ArgumentException>(() => AppScopeResolver.Resolve([tree.Root], "contoso_typo"));

        Assert.Contains("'contoso_typo'", ex.Message);
        Assert.Contains("contoso_sales, contoso_support", ex.Message);
    }

    [Fact]
    public void WhenNoAppModulesExistAtAll_TheErrorSaysWhereTheyAreLookedFor()
    {
        using var tree = new TestTree();
        tree.Declarations("Model/Declarations", null, "contoso_thing");

        var ex = Assert.Throws<ArgumentException>(() => AppScopeResolver.Resolve([tree.Root], "contoso_app"));

        Assert.Contains("--root", ex.Message);
    }

    [Theory]
    [InlineData("node_modules")]
    [InlineData("bin")]
    [InlineData("obj")]
    public void AppModulesInThrowawayDirectories_AreNotRead(string throwaway)
    {
        using var tree = new TestTree();
        tree.AppModule($"Apps/{throwaway}/Copy/Declarations", "contoso_app", "contoso_app", TestTree.Component("1", "contoso_thing"));

        Assert.Throws<ArgumentException>(() => AppScopeResolver.Resolve([tree.Root], "contoso_app"));
    }
}
