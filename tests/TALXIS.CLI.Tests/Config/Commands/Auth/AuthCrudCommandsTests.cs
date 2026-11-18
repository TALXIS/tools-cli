using System.Text.Json;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Features.Config.Auth;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core;
using Xunit;
using ProfileModel = TALXIS.CLI.Core.Model.Profile;
using ConnectionModel = TALXIS.CLI.Core.Model.Connection;

namespace TALXIS.CLI.Tests.Config.Commands.Auth;

[Collection("TxcServicesSerial")]
public sealed class AuthCrudCommandsTests
{
    private static async Task SeedAsync(ICredentialStore store, params Credential[] creds)
    {
        foreach (var c in creds)
            await store.UpsertAsync(c, default);
    }

    [Fact]
    public async Task List_EmitsJsonArray_OfStoredCredentials()
    {
        using var host = new CommandTestHost();
        var store = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;
        await SeedAsync(store,
            new Credential { Id = "dev", Kind = CredentialKind.InteractiveBrowser, TenantId = "t1" },
            new Credential { Id = "prod", Kind = CredentialKind.ClientSecret, TenantId = "t2", ApplicationId = "app2" });

        var sw = new StringWriter();
        using (OutputWriter.RedirectTo(sw))
        {
            var exit = await new AuthListCliCommand().RunAsync();
            Assert.Equal(0, exit);
        }

        using var doc = JsonDocument.Parse(sw.ToString());
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(2, doc.RootElement.GetArrayLength());
        var ids = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToHashSet();
        Assert.Contains("dev", ids);
        Assert.Contains("prod", ids);
    }

    [Fact]
    public async Task Show_ReturnsCredentialJson_WhenFound()
    {
        using var host = new CommandTestHost();
        var store = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;
        await SeedAsync(store,
            new Credential { Id = "dev", Kind = CredentialKind.InteractiveBrowser, TenantId = "t1", Description = "Dev tenant" });

        var sw = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(sw))
            exit = await new AuthGetCliCommand { Alias = "dev" }.RunAsync();

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(sw.ToString());
        Assert.Equal("dev", doc.RootElement.GetProperty("id").GetString());
        Assert.Equal("Dev tenant", doc.RootElement.GetProperty("description").GetString());
    }

    [Fact]
    public async Task Show_ResolvesManagedIdentityDetailsFromToken()
    {
        var tokens = new FakeTokens(new { tid = "tenant-mi", appid = "client-mi", oid = "object-mi", exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() });
        using var host = new CommandTestHost(accessTokens: tokens);
        var store = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;
        await SeedAsync(store, new Credential { Id = "lab", Kind = CredentialKind.ManagedIdentity, Cloud = CloudInstance.Public });

        var sw = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(sw))
            exit = await new AuthGetCliCommand { Alias = "lab", CheckToken = true }.RunAsync();

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(sw.ToString());
        Assert.Equal("tenant-mi", doc.RootElement.GetProperty("tenantId").GetString());
        Assert.Equal("client-mi", doc.RootElement.GetProperty("applicationId").GetString());
        Assert.Equal("object-mi", doc.RootElement.GetProperty("objectId").GetString());
        var health = doc.RootElement.GetProperty("tokenHealth");
        Assert.Equal("valid", health.GetProperty("status").GetString());
        Assert.Equal("https://service.powerapps.com/", health.GetProperty("resource").GetString());
        Assert.All(tokens.Resources, r => Assert.Equal("https://service.powerapps.com/", r.ToString()));

        // Read-only: resolved values are not persisted.
        var stored = await store.GetAsync("lab", default);
        Assert.Null(stored!.TenantId);
    }

    [Fact]
    public async Task Show_DoesNotAcquireToken_ForNonManagedIdentityWithoutCheckToken()
    {
        var tokens = new FakeTokens(new { tid = "x" });
        using var host = new CommandTestHost(accessTokens: tokens);
        var store = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;
        await SeedAsync(store, new Credential { Id = "spn", Kind = CredentialKind.ClientSecret, TenantId = "t", ApplicationId = "a" });

        using (OutputWriter.RedirectTo(new StringWriter()))
            Assert.Equal(0, await new AuthGetCliCommand { Alias = "spn" }.RunAsync());

        Assert.Empty(tokens.Resources);
    }

    private sealed class FakeTokens(object claims) : IAccessTokenService
    {
        public List<Uri> Resources { get; } = new();

        public Task<string> AcquireForResourceAsync(ConnectionModel connection, Credential credential, Uri resourceUri, CancellationToken ct)
        {
            Resources.Add(resourceUri);
            var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(claims))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return Task.FromResult($"e30.{payload}.sig");
        }
    }

    [Fact]
    public async Task Show_ReturnsTwo_WhenAliasMissing()
    {
        using var host = new CommandTestHost();
        var exit = await new AuthGetCliCommand { Alias = "nope" }.RunAsync();
        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task Delete_RemovesCredentialAndVaultSecret()
    {
        using var host = new CommandTestHost();
        var store = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;
        var cred = new Credential
        {
            Id = "spn",
            Kind = CredentialKind.ClientSecret,
            TenantId = "t",
            ApplicationId = "app",
            SecretRef = SecretRef.Create("spn", "client-secret"),
        };
        await SeedAsync(store, cred);
        await host.Vault.SetSecretAsync(cred.SecretRef, "super-secret", default);

        var exit = await new AuthDeleteCliCommand { Alias = "spn" }.RunAsync();
        Assert.Equal(0, exit);

        var listed = await store.ListAsync(default);
        Assert.Empty(listed);
        Assert.Empty(host.Vault.Contents);
    }

    [Fact]
    public async Task Delete_ReturnsTwo_WhenAliasMissing()
    {
        using var host = new CommandTestHost();
        var exit = await new AuthDeleteCliCommand { Alias = "nope" }.RunAsync();
        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task Delete_LeavesProfilesOrphaned_WithWarning()
    {
        using var host = new CommandTestHost();
        var credStore = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;
        var profileStore = (IProfileStore)host.Provider.GetService(typeof(IProfileStore))!;

        await SeedAsync(credStore, new Credential { Id = "dev", Kind = CredentialKind.InteractiveBrowser });
        await profileStore.UpsertAsync(
            new ProfileModel { Id = "demo", ConnectionRef = "conn", CredentialRef = "dev" },
            default);

        var exit = await new AuthDeleteCliCommand { Alias = "dev" }.RunAsync();
        Assert.Equal(0, exit);

        // Profile still present, just orphaned.
        var profiles = await profileStore.ListAsync(default);
        Assert.Single(profiles);
        Assert.Equal("dev", profiles[0].CredentialRef);
    }
}
