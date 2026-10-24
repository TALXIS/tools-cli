using DotMake.CommandLine;
using TALXIS.CLI.Features.Config.Profile;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Commands.Profile;

/// <summary>
/// Arguments documented as optional must parse when omitted (#214). DotMake doesn't carry
/// <c>Required = false</c> over to an argument, so these go through the real command tree
/// instead of setting properties on the command directly.
/// </summary>
public sealed class OptionalArgumentParseTests
{
    [Theory]
    [InlineData("config", "profile", "validate")]
    [InlineData("config", "profile", "get")]
    [InlineData("config", "profile", "pin")]
    [InlineData("environment", "data", "query", "fetchxml")]
    public void OptionalArgument_Omitted_Parses(params string[] args)
    {
        var result = Cli.Parse<TALXIS.CLI.TxcCliCommand>(args);

        Assert.Empty(result.ParseResult.Errors);
    }

    [Fact]
    public void ProfileValidate_WithName_BindsIt()
    {
        var result = Cli.Parse<TALXIS.CLI.TxcCliCommand>(new[] { "config", "profile", "validate", "contoso-dev" });

        Assert.Empty(result.ParseResult.Errors);
        Assert.Equal("contoso-dev", result.Bind<ProfileValidateCliCommand>().Name);
    }
}
