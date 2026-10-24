using TALXIS.CLI.MCP;
using Xunit;

namespace TALXIS.CLI.Tests.MCP;

public class TestingBindingsCatalogTests
{
    private readonly TestingBindingsCatalog _catalog;

    public TestingBindingsCatalogTests()
    {
        _catalog = new TestingBindingsCatalog();
        _catalog.Load();
    }

    [Fact]
    public void Load_DiscoversStepBindingsFromTestKitAssembly()
    {
        Assert.True(_catalog.Count > 0, "Expected step bindings to be discovered via reflection");
    }

    [Fact]
    public void GetCatalogPrompt_NamesPlaceholdersFromMethodParameters()
    {
        var prompt = _catalog.GetCatalogPrompt();

        // e.g. "When I open the {index} record in the grid" — not a generic "{value}"
        Assert.Contains("{index}", prompt);
    }

    [Fact]
    public void GetCatalogPrompt_DoesNotLeakUnconvertedCapturingGroupSyntax()
    {
        var prompt = _catalog.GetCatalogPrompt();

        // Every top-level capturing group must become a "{name}" placeholder — none should
        // survive as raw regex in what's presented as literal Gherkin.
        Assert.DoesNotContain("(.*)", prompt);
        Assert.DoesNotContain("([^']+)", prompt);
    }

    [Fact]
    public void GetCatalogPrompt_PreservesNonCapturingGroupsVerbatim()
    {
        var prompt = _catalog.GetCatalogPrompt();

        // Non-capturing alternations aren't parameters and must stay as-is rather than being
        // mistaken for a placeholder.
        Assert.Contains("(?:currency|numeric|text)", prompt);
    }

    [Fact]
    public void GetCatalogPrompt_IncludesResolvedAssemblyVersion()
    {
        var prompt = _catalog.GetCatalogPrompt();

        Assert.Contains("TALXIS.TestKit.Bindings v", prompt);
    }

    [Fact]
    public void GetCatalogPrompt_IncludesDescriptionsFromXmlDocs()
    {
        var prompt = _catalog.GetCatalogPrompt();

        Assert.Contains("*Opens a sub-area from the navigation bar.*", prompt);
    }

    [Fact]
    public void Entries_NeverUseStepAsALiteralGherkinKeyword()
    {
        // "Step" is not a Gherkin keyword; [StepDefinition] bindings (Given/When/Then-agnostic)
        // must map to '*' instead so a copied line still parses.
        Assert.DoesNotContain(_catalog.Entries, e => e.StepType == "Step");
    }
}
