using TALXIS.CLI.Features.Environment.Diagnostics;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Diagnostics;

public class EnvLogFilterBuilderTests
{
    [Theory]
    [InlineData("abc")]
    [InlineData("10x")]
    [InlineData("h")]
    [InlineData("-5d")]
    public void TryBuild_RejectsMalformedSince(string since)
    {
        var ok = EnvLogFilterBuilder.TryBuild(since, null, null, false, null, null, out _, out var error);

        Assert.False(ok);
        Assert.Contains("--since", error);
    }

    [Fact]
    public void TryBuild_RejectsMalformedCorrelationId()
    {
        var ok = EnvLogFilterBuilder.TryBuild(null, null, null, false, "not-a-guid", null, out _, out var error);

        Assert.False(ok);
        Assert.Contains("--correlation-id", error);
    }

    [Fact]
    public void TryBuild_DefaultsTopTo50WithoutSince()
    {
        var ok = EnvLogFilterBuilder.TryBuild(null, null, null, false, null, null, out var filter, out _);

        Assert.True(ok);
        Assert.Equal(50, filter.Top);
        Assert.Null(filter.SinceUtc);
    }

    [Fact]
    public void TryBuild_DefaultsTopTo200WithSince()
    {
        var ok = EnvLogFilterBuilder.TryBuild("24h", null, null, false, null, null, out var filter, out _);

        Assert.True(ok);
        Assert.Equal(200, filter.Top);
        Assert.NotNull(filter.SinceUtc);
    }

    [Fact]
    public void TryBuild_HonoursExplicitTop()
    {
        var ok = EnvLogFilterBuilder.TryBuild("24h", null, null, false, null, 5, out var filter, out _);

        Assert.True(ok);
        Assert.Equal(5, filter.Top);
    }

    [Fact]
    public void TryBuild_ParsesFiltersAndCorrelationId()
    {
        var id = Guid.NewGuid();
        var ok = EnvLogFilterBuilder.TryBuild(null, " account ", " Acme.Plugin ", true, id.ToString(), null, out var filter, out _);

        Assert.True(ok);
        Assert.Equal("account", filter.Entity);
        Assert.Equal("Acme.Plugin", filter.Plugin);
        Assert.True(filter.ErrorsOnly);
        Assert.Equal(id, filter.CorrelationId);
    }
}
