using TALXIS.CLI.Core.Resolution;
using TALXIS.CLI.Core.Vault;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Vault;

public sealed class VaultOptionsTests
{
    private sealed class StubEnv : IEnvironmentReader
    {
        private readonly Dictionary<string, string?> _vars;
        public StubEnv(Dictionary<string, string?>? vars = null)
            => _vars = vars ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        public string? Get(string name) => _vars.TryGetValue(name, out var v) ? v : null;
        public string GetCurrentDirectory() => Directory.GetCurrentDirectory();
    }

    [Fact]
    public void Secrets_UsesLockedFilenameAndAccount()
    {
        var o = VaultOptions.Secrets(new StubEnv());
        Assert.Equal("txc.secrets.v1.dat", o.CacheFileName);
        Assert.Equal("secrets", o.KeychainAccount);
        Assert.Equal("TXC_Secret_Vault", o.LinuxCacheKind);
        Assert.Equal("TALXIS CLI secrets", o.LinuxKeyringLabel);
    }

    [Fact]
    public void MsalTokenCache_UsesLockedFilenameAndAccount()
    {
        var o = VaultOptions.MsalTokenCache(new StubEnv());
        Assert.Equal("txc.msal.tokens.v1.dat", o.CacheFileName);
        Assert.Equal("msal-tokens", o.KeychainAccount);
        Assert.Equal("TXC_Msal_Token_Cache", o.LinuxCacheKind);
    }

    [Fact]
    public void FallbackCacheFileName_AppendsFallbackBeforeExtension()
    {
        var o = VaultOptions.Secrets(new StubEnv());
        Assert.Equal("txc.secrets.v1.fallback.dat", o.FallbackCacheFileName);
    }

    [Fact]
    public void Secrets_HonorsLinuxPlaintextEnvVar_OnLinux()
    {
        if (!OperatingSystem.IsLinux())
            return;
        var env = new StubEnv(new Dictionary<string, string?> { [VaultOptions.LinuxPlaintextEnvVar] = "1" });
        var o = VaultOptions.Secrets(env);
        Assert.True(o.UsePlaintextFallback);
        Assert.Contains("TXC_PLAINTEXT_FALLBACK", o.PlaintextReason);
    }

    [Fact]
    public void Secrets_HonorsMacFileModeEnvVar_OnMac()
    {
        if (!OperatingSystem.IsMacOS())
            return;
        var env = new StubEnv(new Dictionary<string, string?> { [VaultOptions.MacFileModeEnvVar] = "file" });
        var o = VaultOptions.Secrets(env);
        Assert.True(o.UsePlaintextFallback);
        Assert.Contains("TXC_TOKEN_CACHE_MODE=file", o.PlaintextReason);
    }

    [Fact]
    public void Secrets_IgnoresEnvVars_OnWindows()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var env = new StubEnv(new Dictionary<string, string?>
        {
            [VaultOptions.LinuxPlaintextEnvVar] = "1",
            [VaultOptions.MacFileModeEnvVar] = "file",
        });
        var o = VaultOptions.Secrets(env);
        Assert.False(o.UsePlaintextFallback);
    }

    [Fact]
    public void Secrets_ReturnsNotPlaintext_ByDefault()
    {
        var o = VaultOptions.Secrets(new StubEnv());
        Assert.False(o.UsePlaintextFallback);
        Assert.Null(o.PlaintextReason);
    }
}
