using DotMake.CommandLine;
using TALXIS.CLI.Features.Environment.Entity;
using Xunit;

namespace TALXIS.CLI.Tests.Environment;

/// <summary>
/// The rest of <c>environment entity</c> follows the delete shape: the attribute or entity
/// acted on is positional and the owning entity is <c>--entity</c>.
/// </summary>
[Collection("CliParserSerial")]
public sealed class EntityCommandArgumentShapeTests
{
    private static CliResult Parse(params string[] args) => Cli.Parse<TALXIS.CLI.TxcCliCommand>(args);

    [Fact]
    public void EntityUpdate_TakesEntityPositionally()
    {
        var result = Parse("environment", "entity", "update", "tom_project", "--display-name", "Initiative", "--apply");

        Assert.Empty(result.ParseResult.Errors);
        Assert.Equal("tom_project", result.Bind<EntityUpdateCliCommand>().Entity);
    }

    [Fact]
    public void AttributeGet_TakesAttributePositionally_AndEntityAsOption()
    {
        var result = Parse("environment", "entity", "attribute", "get", "name", "--entity", "account");

        Assert.Empty(result.ParseResult.Errors);
        var command = result.Bind<EntityAttributeGetCliCommand>();
        Assert.Equal("name", command.Name);
        Assert.Equal("account", command.Entity);
    }

    [Fact]
    public void AttributeUpdate_TakesAttributePositionally_AndEntityAsOption()
    {
        var result = Parse("environment", "entity", "attribute", "update", "name", "--entity", "account", "--required", "required", "--apply");

        Assert.Empty(result.ParseResult.Errors);
        var command = result.Bind<EntityAttributeUpdateCliCommand>();
        Assert.Equal("name", command.Name);
        Assert.Equal("account", command.Entity);
    }

    [Fact]
    public void AttributeCreate_TakesEntityAsOption()
    {
        var result = Parse("environment", "entity", "attribute", "create", "--entity", "tom_project", "--name", "tom_code", "--type", "string", "--apply");

        Assert.Empty(result.ParseResult.Errors);
        var command = result.Bind<EntityAttributeCreateCliCommand>();
        Assert.Equal("tom_project", command.Entity);
        Assert.Equal("tom_code", command.Name);
    }

    [Theory]
    [InlineData("environment", "entity", "update", "--entity", "tom_project", "--display-name", "Initiative", "--apply")]
    [InlineData("environment", "entity", "attribute", "get", "account", "--name", "name")]
    [InlineData("environment", "entity", "attribute", "update", "account", "--name", "name", "--apply")]
    [InlineData("environment", "entity", "attribute", "create", "tom_project", "--name", "tom_code", "--type", "string", "--apply")]
    public void OldShapes_AreRejected(params string[] args)
    {
        Assert.NotEmpty(Parse(args).ParseResult.Errors);
    }
}
