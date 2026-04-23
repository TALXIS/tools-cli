using TALXIS.CLI.Core.Model;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Storage;

public class SecretRefTests
{
    [Fact]
    public void FormatsCanonicalUri()
    {
        var r = SecretRef.Create("ci-spn", "client-secret");
        Assert.Equal("vault://com.talxis.txc/ci-spn/client-secret", r.Uri);
        Assert.Equal(r.Uri, r.ToString());
    }

    [Theory]
    [InlineData("vault://com.talxis.txc/ci-spn/client-secret", "ci-spn", "client-secret")]
    [InlineData("vault://com.talxis.txc/cred.1/pat", "cred.1", "pat")]
    public void ParsesValidUris(string input, string expectedId, string expectedSlot)
    {
        var r = SecretRef.Parse(input);
        Assert.Equal(expectedId, r.CredentialId);
        Assert.Equal(expectedSlot, r.Slot);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("https://example/x/y")]
    [InlineData("vault://other-service/x/y")]
    [InlineData("vault://com.talxis.txc/only-one-segment")]
    [InlineData("vault://com.talxis.txc/a/b/c")]
    public void RejectsInvalidUris(string? input)
    {
        Assert.False(SecretRef.TryParse(input, out _));
        Assert.Throws<FormatException>(() => SecretRef.Parse(input ?? ""));
    }

    [Fact]
    public void RoundTripsThroughParse()
    {
        var original = SecretRef.Create("my-cred", "certificate-password");
        var parsed = SecretRef.Parse(original.Uri);
        Assert.Equal(original, parsed);
    }
}
