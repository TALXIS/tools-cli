using TALXIS.CLI.Core.Vault;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Vault;

public sealed class VaultUnavailableExceptionTests
{
    [Fact]
    public void DefaultMessage_ContainsCanonicalRemedy()
    {
        var ex = new VaultUnavailableException();
        Assert.Equal(VaultUnavailableException.RemedyMessage, ex.Message);
    }

    [Fact]
    public void Message_MentionsLibsecretAndPlaintextOptIns()
    {
        var ex = new VaultUnavailableException();
        Assert.Contains("libsecret-1-0", ex.Message);
        Assert.Contains("TXC_PLAINTEXT_FALLBACK", ex.Message);
        Assert.Contains("--plaintext-fallback", ex.Message);
    }

    [Fact]
    public void Constructor_PreservesInnerException()
    {
        var inner = new InvalidOperationException("root cause");
        var ex = new VaultUnavailableException(inner);
        Assert.Same(inner, ex.InnerException);
    }
}
