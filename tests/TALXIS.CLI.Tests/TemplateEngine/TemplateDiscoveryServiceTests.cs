using System.Linq;
using Microsoft.TemplateEngine.Abstractions;
using TALXIS.CLI.Features.Workspace.TemplateEngine;
using Xunit;

namespace TALXIS.CLI.Tests.TemplateEngine;

public class TemplateDiscoveryServiceTests
{
    [Fact]
    public void SelectUserParameters_TemplateWithOnlyName_YieldsName()
    {
        ITemplateParameter[] definitions =
        {
            new FakeTemplateParameter("type"),
            new FakeTemplateParameter("language"),
            new FakeTemplateParameter("name", isName: true),
        };

        var selected = TemplateDiscoveryService.SelectUserParameters(definitions);

        Assert.Equal(new[] { "name" }, selected.Select(p => p.Name));
    }

    [Fact]
    public void SelectUserParameters_KeepsTemplateSymbolsInDeclaredOrder()
    {
        ITemplateParameter[] definitions =
        {
            new FakeTemplateParameter("name", isName: true),
            new FakeTemplateParameter("EntitySchemaName"),
            new FakeTemplateParameter("type"),
            new FakeTemplateParameter("EntityType"),
        };

        var selected = TemplateDiscoveryService.SelectUserParameters(definitions);

        Assert.Equal(new[] { "name", "EntitySchemaName", "EntityType" }, selected.Select(p => p.Name));
    }
}
