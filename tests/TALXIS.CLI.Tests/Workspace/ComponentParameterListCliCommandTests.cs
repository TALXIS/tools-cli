using System.Collections.Generic;
using System.Linq;
using Microsoft.TemplateEngine.Abstractions;
using TALXIS.CLI.Features.Workspace;
using TALXIS.CLI.Tests.TemplateEngine;
using Xunit;

namespace TALXIS.CLI.Tests.Workspace;

public class ComponentParameterListCliCommandTests
{
    private static object? Field(object row, string field) => row.GetType().GetProperty(field)!.GetValue(row);

    private static IEnumerable<string> Names(IEnumerable<object> rows) => rows.Select(r => (string)Field(r, "name")!);

    [Fact]
    public void ProjectParameters_TemplateWithOnlyName_ListsOutputThenName()
    {
        ITemplateParameter[] parameters = { new FakeTemplateParameter("name", isName: true) };

        var rows = ComponentParameterListCliCommand.ProjectParameters(parameters);

        Assert.Equal(new[] { "output", "name" }, Names(rows));
        Assert.Equal(false, Field(rows[1], "required"));
        Assert.Contains("output folder name", (string)Field(rows[1], "description")!);
    }

    [Fact]
    public void ProjectParameters_PutsNameBeforeTemplateSymbols()
    {
        ITemplateParameter[] parameters =
        {
            new FakeTemplateParameter("EntitySchemaName"),
            new FakeTemplateParameter("name", isName: true),
            new FakeTemplateParameter("EntityType"),
        };

        var rows = ComponentParameterListCliCommand.ProjectParameters(parameters);

        Assert.Equal(new[] { "output", "name", "EntitySchemaName", "EntityType" }, Names(rows));
    }

    [Fact]
    public void ProjectParameters_SymbolNamedName_HidesTheImplicitNameRow()
    {
        // --param keys are case-insensitive, so pp-pcf's 'Name' symbol and the implicit 'name' share one key
        ITemplateParameter[] parameters =
        {
            new FakeTemplateParameter("name", isName: true),
            new FakeTemplateParameter("Name"),
            new FakeTemplateParameter("Namespace"),
        };

        var rows = ComponentParameterListCliCommand.ProjectParameters(parameters);

        Assert.Equal(new[] { "output", "Name", "Namespace" }, Names(rows));
    }

    [Fact]
    public void ProjectParameters_TemplateWithoutName_DoesNotInventIt()
    {
        var rows = ComponentParameterListCliCommand.ProjectParameters(new ITemplateParameter[0]);

        Assert.Equal(new[] { "output" }, Names(rows));
    }
}
