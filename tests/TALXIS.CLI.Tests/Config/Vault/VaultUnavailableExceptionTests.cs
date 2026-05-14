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
    public void Message_MentionsPlatformSpecificRemedies()
    {
        var ex = new VaultUnavailableException();
        // Linux remedy
        Assert.Contains("libsecret-1-0", ex.Message);
        // macOS remedy
        Assert.Contains("TXC_TOKEN_CACHE_MODE=file", ex.Message);
        // Cross-platform fallback
        Assert.Contains("TXC_PLAINTEXT_FALLBACK", ex.Message);
    }

    [Fact]
    public void Constructor_PreservesInnerException()
    {
        var inner = new InvalidOperationException("root cause");
        var ex = new VaultUnavailableException(inner);
        Assert.Same(inner, ex.InnerException);
    }
}
