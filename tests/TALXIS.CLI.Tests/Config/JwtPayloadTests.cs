using System.Text;
using TALXIS.CLI.Core.Identity;
using Xunit;

namespace TALXIS.CLI.Tests.Config;

public class JwtPayloadTests
{
    private static string Token(string json)
        => "header." + Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".signature";

    [Fact]
    public void TryParse_ReadsAnUnpaddedBase64UrlPayload()
    {
        using var payload = JwtPayload.TryParse(Token("{\"oid\":\"abc\",\"note\":\"??>>\"}"));

        Assert.Equal("abc", payload!.RootElement.GetProperty("oid").GetString());
        Assert.Equal("??>>", payload.RootElement.GetProperty("note").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("a.!!!.c")]
    [InlineData("a.bm90LWpzb24.c")]
    public void TryParse_ReturnsNullForAnythingElse(string? token)
        => Assert.Null(JwtPayload.TryParse(token));
}
