using System.Text.Json;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Bootstrapping;
using TALXIS.CLI.Features.Config.Auth;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Commands.Auth;

[Collection("TxcServicesSerial")]
public sealed class AuthLoginCommandTests
{
    [Fact]
    public async Task Login_PersistsCredential_WithUpnAlias()
    {
        using var host = new CommandTestHost(
            loginResult: new InteractiveLoginResult("tomas@contoso.com", "t-guid"));
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
