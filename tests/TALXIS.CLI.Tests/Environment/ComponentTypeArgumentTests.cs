using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.CLI.Features.Environment.Component;
using Xunit;

namespace TALXIS.CLI.Tests.Environment;

public sealed class ComponentTypeArgumentTests
{
    [Theory]
    [InlineData("Entity", 1)]
    [InlineData("1", 1)]
    public void KnownTypeResolvesToItsCode(string value, int expectedCode)
    {
        var resolved = ComponentTypeArgument.TryResolve(value, NullLogger.Instance);

        Assert.NotNull(resolved);
        Assert.Equal(expectedCode, resolved.Value.Code);
        Assert.Equal("Entity", resolved.Value.Name);
    }

    [Fact]
    public void UnknownIntegerCodeIsPassedThrough()
    {
        var resolved = ComponentTypeArgument.TryResolve("10164", NullLogger.Instance);

        Assert.NotNull(resolved);
        Assert.Equal(10164, resolved.Value.Code);
        Assert.Equal("10164", resolved.Value.Name);
    }

    [Theory]
    [InlineData("NotAType")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("")]
    public void InvalidValueIsRejected(string value)
    {
        Assert.Null(ComponentTypeArgument.TryResolve(value, NullLogger.Instance));
    }
}
