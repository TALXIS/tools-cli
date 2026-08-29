using TALXIS.CLI.Core.Headless;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Headless;

public class EnvironmentAuthRequiredExceptionTests
{
    private const string Url = "https://devbox-2782.crm4.dynamics.com";

    [Fact]
    public void Message_NamesEnvironmentUrl_AndFixCommand()
    {
        var ex = new EnvironmentAuthRequiredException(Url);

        Assert.Contains("https://devbox-2782.crm4.dynamics.com/", ex.Message);
        Assert.Contains("txc config profile create --url https://devbox-2782.crm4.dynamics.com/", ex.Message);
    }

    [Fact]
    public void Message_TellsUserToRunManuallyInInteractiveTerminal()
    {
        var ex = new EnvironmentAuthRequiredException(Url);

        Assert.Contains("interactive terminal", ex.Message);
        Assert.Contains("human in the loop", ex.Message);
    }

    [Fact]
    public void Message_NoEmDash()
    {
        var ex = new EnvironmentAuthRequiredException(Url);

        Assert.DoesNotContain('—', ex.Message);
    }

    [Fact]
    public void Message_AppendsHeadlessReason_WhenProvided()
    {
        var ex = new EnvironmentAuthRequiredException(Url, headlessReason: "CI=true");

        Assert.Contains("CI=true", ex.Message);
        Assert.Equal("CI=true", ex.HeadlessReason);
    }

    [Fact]
    public void Message_OmitsHeadlessReason_WhenInteractive()
    {
        var ex = new EnvironmentAuthRequiredException(Url, headlessReason: null);

        Assert.DoesNotContain("non-interactive (", ex.Message);
        Assert.Null(ex.HeadlessReason);
    }

    [Theory]
    [InlineData("https://contoso.crm4.dynamics.com")]
    [InlineData("https://contoso.crm4.dynamics.com/")]
    [InlineData("https://contoso.crm4.dynamics.com/main.aspx")]
    public void Message_NormalizesUrlToAuthorityWithTrailingSlash(string input)
    {
        var ex = new EnvironmentAuthRequiredException(input);

        Assert.Contains("https://contoso.crm4.dynamics.com/", ex.Message);
        Assert.DoesNotContain("main.aspx", ex.Message);
    }

    [Fact]
    public void Message_FallsBack_WhenUrlMissing()
    {
        var ex = new EnvironmentAuthRequiredException(environmentUrl: "");

        Assert.Contains("the target environment", ex.Message);
        Assert.Contains("--url <environment-url>", ex.Message);
    }

    [Fact]
    public void Preserves_InnerException()
    {
        var inner = new InvalidOperationException("token expired");
        var ex = new EnvironmentAuthRequiredException(Url, innerException: inner);

        Assert.Same(inner, ex.InnerException);
    }
}
