using System.Text.Json;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Bootstrapping;
using TALXIS.CLI.Features.Config.Auth;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Identity;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Commands.Auth;

[Collection("TxcServicesSerial")]
public sealed class AuthLoginCommandTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("azure-host")]
    public async Task ManagedIdentity_RegistersAndReusesSecretlessCredential_Headlessly(string? alias)
    {
        using var host = new CommandTestHost(headless: true);
        var store = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;
        using var output = new StringWriter();
        using (OutputWriter.RedirectTo(output))
        {
            Assert.Equal(0, await new AuthLoginCliCommand { ManagedIdentity = true, Alias = alias }.RunAsync());
            var original = Assert.Single(await store.ListAsync(default));
            Assert.Equal(0, await new AuthLoginCliCommand { ManagedIdentity = true, Alias = alias }.RunAsync());
            var credential = Assert.Single(await store.ListAsync(default));

            Assert.Equal(alias ?? "managed-identity", credential.Id);
            Assert.Equal(CredentialKind.ManagedIdentity, credential.Kind);
            Assert.Equal(original.CreatedAt, credential.CreatedAt);
            Assert.Null(credential.ApplicationId);
            Assert.Null(credential.SecretRef);
            Assert.Null(credential.InteractiveUpn);
            Assert.Null(credential.InteractiveAccountId);
            Assert.Equal(0, host.Login.Calls);
            Assert.Empty(host.Vault.Contents);
        }
    }

    [Fact]
    public async Task ManagedIdentity_DefaultAlias_DoesNotReplaceAnotherCredentialKind()
    {
        using var host = new CommandTestHost(headless: true);
        var store = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;
        await store.UpsertAsync(new Credential { Id = "managed-identity", Kind = CredentialKind.ClientSecret }, default);

        using (OutputWriter.RedirectTo(new StringWriter()))
        {
            Assert.Equal(0, await new AuthLoginCliCommand { ManagedIdentity = true }.RunAsync());
            Assert.Equal(0, await new AuthLoginCliCommand { ManagedIdentity = true }.RunAsync());
        }

        var credentials = await store.ListAsync(default);
        Assert.Equal(2, credentials.Count);
        Assert.Contains(credentials, c => c.Id == "managed-identity" && c.Kind == CredentialKind.ClientSecret);
        Assert.Contains(credentials, c => c.Id == "managed-identity-2" && c.Kind == CredentialKind.ManagedIdentity);
    }

    [Fact]
    public async Task ManagedIdentity_WithDeviceCode_RejectsBeforeSaving()
    {
        using var host = new CommandTestHost();
        using (OutputWriter.RedirectTo(new StringWriter()))
            Assert.NotEqual(0, await new AuthLoginCliCommand { ManagedIdentity = true, DeviceCode = true }.RunAsync());

        var store = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;
        Assert.Empty(await store.ListAsync(default));
        Assert.Equal(0, host.Login.Calls);
    }

    [Fact]
    public async Task ManagedIdentity_ExplicitAlias_RejectsAnotherCredentialKind()
    {
        using var host = new CommandTestHost(headless: true);
        var store = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;
        await store.UpsertAsync(new Credential { Id = "existing", Kind = CredentialKind.ClientSecret }, default);

        using (OutputWriter.RedirectTo(new StringWriter()))
            Assert.NotEqual(0, await new AuthLoginCliCommand { ManagedIdentity = true, Alias = "existing" }.RunAsync());

        Assert.Equal(CredentialKind.ClientSecret, Assert.Single(await store.ListAsync(default)).Kind);
        Assert.Equal(0, host.Login.Calls);
    }

    [Fact]
    public async Task ManagedIdentity_ReusesMatchingTenantAndCloud_WithoutReplacingOtherBindings()
    {
        using var host = new CommandTestHost(headless: true);
        var store = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;

        using (OutputWriter.RedirectTo(new StringWriter()))
        {
            Assert.Equal(0, await new AuthLoginCliCommand { ManagedIdentity = true }.RunAsync());
            for (var i = 0; i < 2; i++)
                Assert.Equal(0, await new AuthLoginCliCommand
                {
                    ManagedIdentity = true,
                    Tenant = "contoso.onmicrosoft.com",
                    Cloud = CloudInstance.GccHigh,
                }.RunAsync());
        }

        var credentials = await store.ListAsync(default);
        Assert.Equal(2, credentials.Count);
        Assert.Contains(credentials, c => c.Cloud == CloudInstance.Public && c.TenantId is null);
        Assert.Contains(credentials, c => c.Cloud == CloudInstance.GccHigh && c.TenantId == "contoso.onmicrosoft.com");
    }

    [Fact]
    public async Task Login_PersistsCredential_WithUpnAlias()
    {
        using var host = new CommandTestHost(
            loginResult: new InteractiveLoginResult(
                "tomas@contoso.com",
                "t-guid",
                "account-guid",
                MsalClientFactory.PublicClientId));
        var store = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;

        var sw = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(sw))
            exit = await new AuthLoginCliCommand().RunAsync();

        Assert.Equal(0, exit);
        Assert.Equal(1, host.Login.Calls);
        Assert.Null(host.Login.LastTenant);
        Assert.Equal(CloudInstance.Public, host.Login.LastCloud);

        var creds = await store.ListAsync(default);
        var cred = Assert.Single(creds);
        Assert.Equal("tomas@contoso.com", cred.Id);
        Assert.Equal(CredentialKind.InteractiveBrowser, cred.Kind);
        Assert.Equal("t-guid", cred.TenantId);
        Assert.Equal(CloudInstance.Public, cred.Cloud);
        Assert.Equal("account-guid", cred.InteractiveAccountId);
        Assert.Equal("tomas@contoso.com", cred.InteractiveUpn);
        Assert.Equal(MsalClientFactory.PublicClientId, cred.ApplicationId);

        using var doc = JsonDocument.Parse(sw.ToString());
        Assert.Equal("tomas@contoso.com", doc.RootElement.GetProperty("id").GetString());
        Assert.Equal("tomas@contoso.com", doc.RootElement.GetProperty("upn").GetString());
    }

    [Fact]
    public async Task Login_HonorsExplicitAliasAndTenantAndCloud()
    {
        using var host = new CommandTestHost(
            loginResult: new InteractiveLoginResult("admin@fabrikam.onmicrosoft.com", "fab-t"));

        var sw = new StringWriter();
        using (OutputWriter.RedirectTo(sw))
        {
            var exit = await new AuthLoginCliCommand
            {
                Alias = "prod",
                Tenant = "fabrikam.onmicrosoft.com",
                Cloud = CloudInstance.GccHigh,
            }.RunAsync();
            Assert.Equal(0, exit);
        }

        Assert.Equal("fabrikam.onmicrosoft.com", host.Login.LastTenant);
        Assert.Equal(CloudInstance.GccHigh, host.Login.LastCloud);

        var store = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;
        var cred = await store.GetAsync("prod", default);
        Assert.NotNull(cred);
        Assert.Equal(CloudInstance.GccHigh, cred!.Cloud);
    }

    [Fact]
    public async Task Login_AppendsTenantShortName_OnAliasCollision()
    {
        using var host = new CommandTestHost(
            loginResult: new InteractiveLoginResult("tomas@contoso.com", "t-guid"));
        var store = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;
        await store.UpsertAsync(
            new Credential { Id = "tomas@contoso.com", Kind = CredentialKind.DeviceCode },
            default);

        var exit = await new AuthLoginCliCommand().RunAsync();
        Assert.Equal(0, exit);

        var creds = await store.ListAsync(default);
        Assert.Equal(2, creds.Count);
        Assert.Contains(creds, c => c.Id == "tomas@contoso.com-contoso");
    }

    [Fact]
    public async Task Login_FallsBackToNumericSuffix_WhenTenantShortNameCollidesToo()
    {
        using var host = new CommandTestHost(
            loginResult: new InteractiveLoginResult("tomas@contoso.com", "t-guid"));
        var store = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;
        await store.UpsertAsync(new Credential { Id = "tomas@contoso.com", Kind = CredentialKind.DeviceCode }, default);
        await store.UpsertAsync(new Credential { Id = "tomas@contoso.com-contoso", Kind = CredentialKind.DeviceCode }, default);

        var exit = await new AuthLoginCliCommand().RunAsync();
        Assert.Equal(0, exit);
        var creds = await store.ListAsync(default);
        Assert.Contains(creds, c => c.Id == "tomas@contoso.com-2");
    }

    [Fact]
    public async Task Login_ReusesExistingInteractiveCredential_ForSameAccount()
    {
        using var host = new CommandTestHost(
            loginResult: new InteractiveLoginResult(
                "tomas@contoso.com",
                "t-guid",
                "account-1",
                MsalClientFactory.PublicClientId));
        var store = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;
        await store.UpsertAsync(new Credential
        {
            Id = "shared",
            Kind = CredentialKind.InteractiveBrowser,
            TenantId = "t-guid",
            Cloud = CloudInstance.Public,
            ApplicationId = MsalClientFactory.PublicClientId,
            InteractiveAccountId = "account-1",
            InteractiveUpn = "tomas@contoso.com",
            Description = "Interactive sign-in (tomas@contoso.com)",
        }, default);

        var sw = new StringWriter();
        using (OutputWriter.RedirectTo(sw))
        {
            var exit = await new AuthLoginCliCommand().RunAsync();
            Assert.Equal(0, exit);
        }

        var creds = await store.ListAsync(default);
        var cred = Assert.Single(creds);
        Assert.Equal("shared", cred.Id);

        using var doc = JsonDocument.Parse(sw.ToString());
        Assert.Equal("shared", doc.RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Login_ReusesLegacyUpnAliasedInteractiveCredential_AndBackfillsIdentity()
    {
        using var host = new CommandTestHost(
            loginResult: new InteractiveLoginResult(
                "tomas@contoso.com",
                "t-guid",
                "account-1",
                MsalClientFactory.PublicClientId));
        var store = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;
        await store.UpsertAsync(new Credential
        {
            Id = "tomas@contoso.com",
            Kind = CredentialKind.InteractiveBrowser,
            TenantId = "t-guid",
            Cloud = CloudInstance.Public,
        }, default);

        var exit = await new AuthLoginCliCommand().RunAsync();
        Assert.Equal(0, exit);

        var creds = await store.ListAsync(default);
        var cred = Assert.Single(creds);
        Assert.Equal("tomas@contoso.com", cred.Id);
        Assert.Equal("account-1", cred.InteractiveAccountId);
        Assert.Equal("tomas@contoso.com", cred.InteractiveUpn);
        Assert.Equal(MsalClientFactory.PublicClientId, cred.ApplicationId);
    }

    [Fact]
    public async Task Login_WithExplicitAlias_ReusesExistingSameAccountCredential()
    {
        using var host = new CommandTestHost(
            loginResult: new InteractiveLoginResult(
                "tomas@contoso.com",
                "t-guid",
                "account-1",
                MsalClientFactory.PublicClientId));
        var store = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;
        await store.UpsertAsync(new Credential
        {
            Id = "shared",
            Kind = CredentialKind.InteractiveBrowser,
            TenantId = "t-guid",
            Cloud = CloudInstance.Public,
            ApplicationId = MsalClientFactory.PublicClientId,
            InteractiveAccountId = "account-1",
            InteractiveUpn = "tomas@contoso.com",
        }, default);

        var exit = await new AuthLoginCliCommand { Alias = "other-name" }.RunAsync();
        Assert.Equal(0, exit);

        var creds = await store.ListAsync(default);
        var cred = Assert.Single(creds);
        Assert.Equal("shared", cred.Id);
        Assert.Null(await store.GetAsync("other-name", default));
    }

    [Fact]
    public async Task Login_DeviceCodeFlag_UsesDeviceCode_EvenWhenBrowserAvailable()
    {
        using var host = new CommandTestHost(browserAvailable: true);
        var store = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;

        using (OutputWriter.RedirectTo(new StringWriter()))
            Assert.Equal(0, await new AuthLoginCliCommand { DeviceCode = true }.RunAsync());

        Assert.Equal(CredentialKind.DeviceCode, Assert.Single(await store.ListAsync(default)).Kind);
    }

    [Fact]
    public async Task Login_DeviceCode_PersistsCredential_WhenFlagExplicit()
    {
        using var host = new CommandTestHost(
            browserAvailable: false,
            loginResult: new InteractiveLoginResult(
                "tomas@contoso.com",
                "t-guid",
                "account-guid",
                MsalClientFactory.PublicClientId));
        var store = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;

        var sw = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(sw))
            exit = await new AuthLoginCliCommand { DeviceCode = true }.RunAsync();

        Assert.Equal(0, exit);
        Assert.Equal(1, host.Login.Calls);

        var creds = await store.ListAsync(default);
        var cred = Assert.Single(creds);
        Assert.Equal("tomas@contoso.com", cred.Id);
        Assert.Equal(CredentialKind.DeviceCode, cred.Kind);

        using var doc = JsonDocument.Parse(sw.ToString());
        Assert.Equal("device-code", doc.RootElement.GetProperty("flow").GetString());
    }

    [Fact]
    public async Task Login_DeviceCode_TriggeredAutomatically_WhenBrowserUnavailable()
    {
        using var host = new CommandTestHost(
            browserAvailable: false,
            loginResult: new InteractiveLoginResult("tomas@contoso.com", "t-guid"));
        var store = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;

        // No --device-code flag; auto-detection via FakeBrowserProbe should trigger device code
        var exit = await new AuthLoginCliCommand().RunAsync();

        Assert.Equal(0, exit);
        Assert.Equal(1, host.Login.Calls);

        var creds = await store.ListAsync(default);
        var cred = Assert.Single(creds);
        Assert.Equal(CredentialKind.DeviceCode, cred.Kind);
    }

    [Fact]
    public async Task Login_FailsFast_InHeadlessEnvironment()
    {
        using var host = new CommandTestHost(headless: true);
        var exit = await new AuthLoginCliCommand().RunAsync();
        Assert.Equal(1, exit);
        Assert.Equal(0, host.Login.Calls);
    }

    [Theory]
    [InlineData("tomas@contoso.com", "contoso")]
    [InlineData("jane@fabrikam.onmicrosoft.com", "fabrikam")]
    [InlineData("ops@tenant-name.co.uk", "tenant-name")]
    [InlineData("noatsign", null)]
    [InlineData("trailing@", null)]
    [InlineData("", null)]
    public void ExtractTenantShortName_HandlesCommonShapes(string upn, string? expected)
    {
        Assert.Equal(expected, CredentialAliasResolver.ExtractTenantShortName(upn));
    }
}
