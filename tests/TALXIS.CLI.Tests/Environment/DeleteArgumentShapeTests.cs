using DotMake.CommandLine;
using TALXIS.CLI.Features.Environment.Data.Record;
using TALXIS.CLI.Features.Environment.Entity;
using TALXIS.CLI.Features.Environment.OptionSet;
using TALXIS.CLI.Features.Environment.Role;
using Xunit;

namespace TALXIS.CLI.Tests.Environment;

/// <summary>
/// Delete commands (and <c>role get</c>) take the object they act on as a positional
/// argument and the parent that scopes it as <c>--entity</c>; the old shapes are rejected.
/// </summary>
[Collection("CliParserSerial")]
public sealed class DeleteArgumentShapeTests
{
    private static CliResult Parse(params string[] args) => Cli.Parse<TALXIS.CLI.TxcCliCommand>(args);

    [Fact]
    public void EntityDelete_TakesEntityPositionally()
    {
        var result = Parse("environment", "entity", "delete", "tom_project", "--apply");

        Assert.Empty(result.ParseResult.Errors);
        Assert.Equal("tom_project", result.Bind<EntityDeleteCliCommand>().Entity);
    }

    [Fact]
    public void AttributeDelete_TakesAttributePositionally_AndEntityAsOption()
    {
        var result = Parse("environment", "entity", "attribute", "delete", "tom_code", "--entity", "tom_project", "--apply");

        Assert.Empty(result.ParseResult.Errors);
        var command = result.Bind<EntityAttributeDeleteCliCommand>();
        Assert.Equal("tom_code", command.Attribute);
        Assert.Equal("tom_project", command.Entity);
    }

    [Fact]
    public void RecordDelete_TakesRecordIdPositionally_AndEntityAsOption()
    {
        var id = Guid.NewGuid();
        var result = Parse("environment", "data", "record", "delete", id.ToString(), "--entity", "account", "--apply");

        Assert.Empty(result.ParseResult.Errors);
        var command = result.Bind<EnvDataRecordDeleteCliCommand>();
        Assert.Equal(id, command.RecordId);
        Assert.Equal("account", command.Entity);
    }

    [Fact]
    public void RelationshipDelete_TakesSchemaNamePositionally()
    {
        var result = Parse("environment", "entity", "relationship", "delete", "tom_project_account", "--apply");

        Assert.Empty(result.ParseResult.Errors);
        Assert.Equal("tom_project_account", result.Bind<EntityRelationshipDeleteCliCommand>().Name);
    }

    [Fact]
    public void OptionSetDelete_TakesNamePositionally()
    {
        var result = Parse("environment", "optionset", "delete", "tom_priority", "--apply");

        Assert.Empty(result.ParseResult.Errors);
        Assert.Equal("tom_priority", result.Bind<OptionSetDeleteCliCommand>().Name);
    }

    [Fact]
    public void RoleGet_TakesRolePositionally()
    {
        var result = Parse("environment", "role", "get", "Catalog Viewer");

        Assert.Empty(result.ParseResult.Errors);
        Assert.Equal("Catalog Viewer", result.Bind<RoleGetCliCommand>().Role);
    }

    [Theory]
    [InlineData("environment", "entity", "delete", "--entity", "tom_project", "--apply")]
    [InlineData("environment", "entity", "attribute", "delete", "tom_project", "--name", "tom_code", "--apply")]
    [InlineData("environment", "entity", "relationship", "delete", "--name", "tom_project_account", "--apply")]
    [InlineData("environment", "optionset", "delete", "--name", "tom_priority", "--apply")]
    [InlineData("environment", "role", "get", "--role", "Catalog Viewer")]
    public void OldShapes_AreRejected(params string[] args)
    {
        Assert.NotEmpty(Parse(args).ParseResult.Errors);
    }
}
