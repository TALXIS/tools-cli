using TALXIS.CLI.Config.Model;
using TALXIS.CLI.Config.Storage;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Storage;

public class CredentialStoreRoundtripTests
{
    [Fact]
    public async Task SecretRefIsSerialisedAsUriString()
    {
        using var dir = new TempConfigDir();
        var store = new CredentialStore(dir.Paths);
        var cred = new Credential
        {
            Id = "ci-spn",
            Kind = CredentialKind.ClientSecret,
            TenantId = "tenant-1",
            ApplicationId = "app-1",
            SecretRef = SecretRef.Create("ci-spn", "client-secret"),
        };
        await store.UpsertAsync(cred, CancellationToken.None);

        var raw = await File.ReadAllTextAsync(dir.Paths.CredentialsFile);
        Assert.Contains("vault://com.talxis.txc/ci-spn/client-secret", raw);
        Assert.Contains("client-secret", raw); // kebab-case enum

        var loaded = await new CredentialStore(dir.Paths).GetAsync("ci-spn", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(CredentialKind.ClientSecret, loaded!.Kind);
        Assert.NotNull(loaded.SecretRef);
        Assert.Equal("ci-spn", loaded.SecretRef!.CredentialId);
        Assert.Equal("client-secret", loaded.SecretRef.Slot);
    }

    [Fact]
    public async Task NullSecretRefIsOmittedFromJson()
    {
        using var dir = new TempConfigDir();
        var store = new CredentialStore(dir.Paths);
        await store.UpsertAsync(new Credential { Id = "interactive", Kind = CredentialKind.InteractiveBrowser }, CancellationToken.None);

        var raw = await File.ReadAllTextAsync(dir.Paths.CredentialsFile);
        Assert.DoesNotContain("secretRef", raw);
    }
}
