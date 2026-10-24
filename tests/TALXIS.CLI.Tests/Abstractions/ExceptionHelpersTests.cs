using System;
using TALXIS.CLI.Abstractions;
using Xunit;

namespace TALXIS.CLI.Tests.Abstractions;

public class ExceptionHelpersTests
{
    private sealed class MarkerException : Exception
    {
        public MarkerException(string message) : base(message) { }
    }

    [Fact]
    public void FindInChain_ReturnsMatch_WhenTopLevel()
    {
        var ex = new MarkerException("top");

        var found = ExceptionHelpers.FindInChain<MarkerException>(ex);

        Assert.Same(ex, found);
    }

    [Fact]
    public void FindInChain_ReturnsMatch_WhenWrapped()
    {
        var marker = new MarkerException("inner");
        var wrapped = new InvalidOperationException("outer", marker);

        var found = ExceptionHelpers.FindInChain<MarkerException>(wrapped);

        Assert.Same(marker, found);
    }

    [Fact]
    public void FindInChain_ReturnsNull_WhenAbsent()
    {
        var ex = new InvalidOperationException("outer", new ArgumentException("inner"));

        var found = ExceptionHelpers.FindInChain<MarkerException>(ex);

        Assert.Null(found);
    }
}
