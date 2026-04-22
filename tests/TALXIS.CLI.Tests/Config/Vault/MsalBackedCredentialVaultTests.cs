using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.CLI.Config.Model;
using TALXIS.CLI.Config.Storage;
using TALXIS.CLI.Config.Vault;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Vault;

/// <summary>
/// Exercises <see cref="MsalBackedCredentialVault"/> against the plaintext-file
/// path so tests do not touch the host Keychain/DPAPI/libsecret. The protected
/// path is covered by MSAL Extensions' own test suite; we only validate our
/// JSON-dictionary shape and DI semantics here.
/// </summary>
public sealed class MsalBackedCredentialVaultTests
{
    private static VaultOptions PlaintextSecrets() => new()
    {
        CacheFileName = "txc.secrets.v1.dat",
        KeychainAccount = "secrets",
        LinuxKeyringLabel = "TALXIS CLI secrets",
        LinuxCacheKind = "TXC_Secret_Vault",
        UsePlaintextFallback = true,
        PlaintextReason = "test",
    };

    private static async Task<MsalBackedCredentialVault> NewVaultAsync(ConfigPaths paths)
        => await MsalBackedCredentialVault.CreateForTestingAsync(
            PlaintextSecrets(),
            paths,
            NullLogger<MsalBackedCredentialVault>.Instance);

    [Fact]
    public async Task GetSecret_ReturnsNull_WhenEmpty()
    {
        using var dir = new TempConfigDir();
        var vault = await NewVaultAsync(dir.Paths);

        var value = await vault.GetSecretAsync(SecretRef.Create("cred1", "client-secret"), CancellationToken.None);

        Assert.Null(value);
    }

    [Fact]
    public async Task SetSecret_ThenGetSecret_Roundtrips()
    {
        using var dir = new TempConfigDir();
        var vault = await NewVaultAsync(dir.Paths);
        var @ref = SecretRef.Create("cred1", "client-secret");

        await vault.SetSecretAsync(@ref, "s3cr3t", CancellationToken.None);
        var value = await vault.GetSecretAsync(@ref, CancellationToken.None);

        Assert.Equal("s3cr3t", value);
    }

    [Fact]
    public async Task SetSecret_OverwritesPriorValue()
    {
        using var dir = new TempConfigDir();
        var vault = await NewVaultAsync(dir.Paths);
        var @ref = SecretRef.Create("cred1", "pat");

        await vault.SetSecretAsync(@ref, "v1", CancellationToken.None);
        await vault.SetSecretAsync(@ref, "v2", CancellationToken.None);

        Assert.Equal("v2", await vault.GetSecretAsync(@ref, CancellationToken.None));
    }

    [Fact]
    public async Task MultipleSecrets_AreIsolatedByCredentialIdAndSlot()
    {
        using var dir = new TempConfigDir();
        var vault = await NewVaultAsync(dir.Paths);

        await vault.SetSecretAsync(SecretRef.Create("cred-a", "client-secret"), "A-cs", CancellationToken.None);
        await vault.SetSecretAsync(SecretRef.Create("cred-a", "pat"), "A-pat", CancellationToken.None);
        await vault.SetSecretAsync(SecretRef.Create("cred-b", "client-secret"), "B-cs", CancellationToken.None);

        Assert.Equal("A-cs", await vault.GetSecretAsync(SecretRef.Create("cred-a", "client-secret"), CancellationToken.None));
        Assert.Equal("A-pat", await vault.GetSecretAsync(SecretRef.Create("cred-a", "pat"), CancellationToken.None));
        Assert.Equal("B-cs", await vault.GetSecretAsync(SecretRef.Create("cred-b", "client-secret"), CancellationToken.None));
    }

    [Fact]
    public async Task DeleteSecret_ReturnsTrue_AndRemovesEntry()
    {
        using var dir = new TempConfigDir();
        var vault = await NewVaultAsync(dir.Paths);
        var @ref = SecretRef.Create("cred1", "client-secret");
        await vault.SetSecretAsync(@ref, "s3cr3t", CancellationToken.None);

        var removed = await vault.DeleteSecretAsync(@ref, CancellationToken.None);

        Assert.True(removed);
        Assert.Null(await vault.GetSecretAsync(@ref, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteSecret_ReturnsFalse_WhenAbsent()
    {
        using var dir = new TempConfigDir();
        var vault = await NewVaultAsync(dir.Paths);

        var removed = await vault.DeleteSecretAsync(SecretRef.Create("missing", "pat"), CancellationToken.None);

        Assert.False(removed);
    }

    [Fact]
    public async Task SetSecret_WritesJsonDictionary_KeyedByCredentialIdAndSlot()
    {
        using var dir = new TempConfigDir();
        var vault = await NewVaultAsync(dir.Paths);

        await vault.SetSecretAsync(SecretRef.Create("cred1", "client-secret"), "hello", CancellationToken.None);

        var fallbackPath = Path.Combine(dir.Paths.AuthDirectory, "txc.secrets.v1.fallback.dat");
        Assert.True(File.Exists(fallbackPath), $"Expected fallback file at {fallbackPath}");
        var json = await File.ReadAllTextAsync(fallbackPath);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("hello", doc.RootElement.GetProperty("cred1::client-secret").GetString());
    }

    [Fact]
    public async Task Vault_UsesDistinctFallbackFilename()
    {
        using var dir = new TempConfigDir();
        var vault = await NewVaultAsync(dir.Paths);

        await vault.SetSecretAsync(SecretRef.Create("cred1", "pat"), "x", CancellationToken.None);

        var protectedPath = Path.Combine(dir.Paths.AuthDirectory, "txc.secrets.v1.dat");
        var fallbackPath = Path.Combine(dir.Paths.AuthDirectory, "txc.secrets.v1.fallback.dat");

        Assert.False(File.Exists(protectedPath), "Protected filename must not be used when in plaintext mode.");
        Assert.True(File.Exists(fallbackPath));
    }

    [Fact]
    public async Task Helper_IsStableAcrossCalls_OnSameVaultInstance()
    {
        using var dir = new TempConfigDir();
        var vault = await NewVaultAsync(dir.Paths);

        await vault.SetSecretAsync(SecretRef.Create("c", "pat"), "1", CancellationToken.None);
        var helperAfterSet = vault.CacheHelper;
        await vault.GetSecretAsync(SecretRef.Create("c", "pat"), CancellationToken.None);
        var helperAfterGet = vault.CacheHelper;

        Assert.Same(helperAfterSet, helperAfterGet);
    }

    [Fact]
    public async Task SetSecret_RejectsEmptyCredentialIdOrSlot()
    {
        using var dir = new TempConfigDir();
        var vault = await NewVaultAsync(dir.Paths);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            vault.SetSecretAsync(new SecretRef { CredentialId = "", Slot = "pat" }, "x", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            vault.SetSecretAsync(new SecretRef { CredentialId = "c", Slot = "" }, "x", CancellationToken.None));
    }

    [Fact]
    public async Task TxcConfigDir_IsolatesVaultWrites()
    {
        using var dirA = new TempConfigDir();
        using var dirB = new TempConfigDir();
        var vaultA = await NewVaultAsync(dirA.Paths);
        var vaultB = await NewVaultAsync(dirB.Paths);

        await vaultA.SetSecretAsync(SecretRef.Create("c", "pat"), "A-value", CancellationToken.None);

        Assert.Equal("A-value", await vaultA.GetSecretAsync(SecretRef.Create("c", "pat"), CancellationToken.None));
        Assert.Null(await vaultB.GetSecretAsync(SecretRef.Create("c", "pat"), CancellationToken.None));
    }
}
