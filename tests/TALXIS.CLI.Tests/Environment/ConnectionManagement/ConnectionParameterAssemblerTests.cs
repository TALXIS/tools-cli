using TALXIS.CLI.Core.Resolution;
using TALXIS.CLI.Features.Environment.Connection;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.ConnectionManagement;

public class ConnectionParameterAssemblerTests
{
    private sealed class Env(Dictionary<string, string> variables) : IEnvironmentReader
    {
        public string? Get(string name) => variables.TryGetValue(name, out var value) ? value : null;
        public string GetCurrentDirectory() => string.Empty;
    }

    private static ConnectionParameterAssembler Assembler(params (string Name, string Value)[] variables)
        => new(new Env(variables.ToDictionary(v => v.Name, v => v.Value)));

    [Fact]
    public void NoValues_IsAnEmptySuccess()
    {
        Assert.True(Assembler().TryAssemble(null, null, out var values, out var error));
        Assert.Empty(values);
        Assert.Null(error);
    }

    [Fact]
    public void Params_SplitOnTheFirstEqualsSign()
    {
        Assert.True(Assembler().TryAssemble(["token:clientId=abc", "note=a=b=c"], null, out var values, out _));

        Assert.Equal("abc", values["token:clientId"]);
        Assert.Equal("a=b=c", values["note"]);
    }

    [Fact]
    public void SecretsFromEnv_AreReadFromTheNamedVariable()
    {
        var assembler = Assembler(("MY_SECRET", "s3cret"));

        Assert.True(assembler.TryAssemble(["token:clientId=abc"], ["token:clientSecret=MY_SECRET"], out var values, out _));

        Assert.Equal("s3cret", values["token:clientSecret"]);
        Assert.Equal("abc", values["token:clientId"]);
    }

    [Theory]
    [InlineData("novalue")]
    [InlineData("=hunter2")]
    [InlineData("")]
    public void MalformedParam_IsRejectedWithoutEchoingTheItem(string item)
    {
        Assert.False(Assembler().TryAssemble([item], null, out _, out var error));

        Assert.Contains("--param expects name=value", error);
        if (item.Length > 0)
            Assert.DoesNotContain(item, error);
    }

    [Fact]
    public void MalformedSecretPair_NamesTheExpectedShape()
    {
        Assert.False(Assembler().TryAssemble(null, ["JUST_A_VARIABLE"], out _, out var error));

        Assert.Contains("--secret-from-env expects name=ENV_VAR", error);
    }

    [Fact]
    public void DuplicateNames_AreRejectedIgnoringCase()
    {
        Assert.False(Assembler().TryAssemble(["a=1", "A=2"], null, out _, out var error));
        Assert.Contains("more than once", error);

        Assert.False(Assembler(("V", "x")).TryAssemble(null, ["a=V", "A=V"], out _, out error));
        Assert.Contains("more than once", error);
    }

    [Fact]
    public void ANameInBothOptions_IsACollision()
    {
        Assert.False(Assembler(("V", "x")).TryAssemble(["token:clientSecret=plain"], ["token:clientSecret=V"], out _, out var error));

        Assert.Contains("both --param and --secret-from-env", error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AMissingOrEmptyVariable_IsRejectedAndNamesTheVariable(bool set)
    {
        var assembler = set ? Assembler(("MY_SECRET", "")) : Assembler();

        Assert.False(assembler.TryAssemble(null, ["token:clientSecret=MY_SECRET"], out _, out var error));

        Assert.Contains("'MY_SECRET'", error);
        Assert.Contains("not set or empty", error);
    }

    [Fact]
    public void ASecretValue_NeverAppearsInAnError()
    {
        var assembler = Assembler(("MY_SECRET", "s3cret"));

        Assert.False(assembler.TryAssemble(["token:clientSecret=plain"], ["token:clientSecret=MY_SECRET"], out _, out var error));

        Assert.DoesNotContain("s3cret", error);
        Assert.DoesNotContain("plain", error);
    }
}
