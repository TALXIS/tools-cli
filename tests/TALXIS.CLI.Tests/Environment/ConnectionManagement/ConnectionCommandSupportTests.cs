using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Features.Environment.Connection;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.ConnectionManagement;

public class ConnectionCommandSupportTests
{
    [Fact]
    public void TypedErrors_GetTheCommandThatHelps()
    {
        Assert.Contains("txc environment connection list", ConnectionCommandSupport.HintFor(new ConnectionNotFoundException("c1")));
        Assert.Contains("txc environment connection permission add", ConnectionCommandSupport.HintFor(new ConnectionAccessDeniedException("c1", "shared_x")));
        Assert.Contains("--connector", ConnectionCommandSupport.HintFor(new AmbiguousConnectionException("c1", ["a", "b"])));
        Assert.Contains("txc environment connector list", ConnectionCommandSupport.HintFor(new ConnectorNotFoundException("x")));
    }

    [Fact]
    public void OtherErrors_NeedNoHint()
        => Assert.Null(ConnectionCommandSupport.HintFor(new ArgumentException("bad input")));

    [Fact]
    public void PlatformMessages_DoNotKnowTheCliGrammar()
    {
        var messages = new Exception[]
        {
            new ConnectionNotFoundException("c1"),
            new ConnectionNotFoundException("c1", "shared_x"),
            new ConnectionAccessDeniedException("c1", "shared_x"),
            new AmbiguousConnectionException("c1", ["a", "b"]),
            new ConnectorNotFoundException("x"),
        }.Select(e => e.Message);

        Assert.All(messages, m =>
        {
            Assert.DoesNotContain("txc ", m);
            Assert.DoesNotContain("--", m);
        });
    }
}
