using System.Text.Json;
using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Features.Config.Profile;
using TALXIS.CLI.Config.Model;
using TALXIS.CLI.Config.Headless;
using TALXIS.CLI.Shared;
using TALXIS.CLI.Tests.Config.Commands;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Bootstrapping;

[Collection("TxcServicesSerial")]
public sealed class ProfileCreateOneLinerTests
{
    [Fact]
    public async Task UrlMode_Bootstraps_Credential_Connection_Profile_AndActivates()
    {
        using var host = new CommandTestHost();

        var sw = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(sw))
            exit = await new ProfileCreateCliCommand
            {
                Url = "https://contoso.crm4.dynamics.com/",
            }.RunAsync();

        Assert.Equal(0, exit);

        var profiles = (IProfileStore)host.Provider.GetService(typeof(IProfileStore))!;
        var connections = (IConnectionStore)host.Provider.GetService(typeof(IConnectionStore))!;
        var creds = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;
        var global = (IGlobalConfigStore)host.Provider.GetService(typeof(IGlobalConfigStore))!;

        var profile = await profiles.GetAsync("contoso", default);
        Assert.NotNull(profile);
        Assert.Equal("contoso", profile!.ConnectionRef);
        var connection = await connections.GetAsync("contoso", default);
        Assert.NotNull(connection);
        Assert.Equal(ProviderKind.Dataverse, connection!.Provider);
        var credential = await creds.GetAsync(profile.CredentialRef!, default);
        Assert.NotNull(credential);
        Assert.Equal(CredentialKind.InteractiveBrowser, credential!.Kind);

        var cfg = await global.LoadAsync(default);
        Assert.Equal("contoso", cfg.ActiveProfile);

        using var doc = JsonDocument.Parse(sw.ToString());
        Assert.Equal("contoso", doc.RootElement.GetProperty("id").GetString());
        Assert.Equal("tomas@contoso.com", doc.RootElement.GetProperty("upn").GetString());
    }

    [Fact]
    public async Task UrlMode_DerivedName_CollidesWithExistingConnection_PicksSuffix()
    {
        using var host = new CommandTestHost();
        var connections = (IConnectionStore)host.Provider.GetService(typeof(IConnectionStore))!;
        await connections.UpsertAsync(new Connection { Id = "contoso", Provider = ProviderKind.Dataverse, EnvironmentUrl = "https://existing" }, default);

        using (OutputWriter.RedirectTo(new StringWriter()))
        {
            var exit = await new ProfileCreateCliCommand
            {
                Url = "https://contoso.crm.dynamics.com/",
            }.RunAsync();
            Assert.Equal(0, exit);
        }

        var profiles = (IProfileStore)host.Provider.GetService(typeof(IProfileStore))!;
        Assert.NotNull(await profiles.GetAsync("contoso-2", default));
    }

    [Fact]
    public async Task UrlMode_UnknownHost_WithoutProvider_Fails()
    {
        using var host = new CommandTestHost();
        using (OutputWriter.RedirectTo(new StringWriter()))
        {
            var exit = await new ProfileCreateCliCommand
            {
                Url = "https://example.invalid/",
            }.RunAsync();
            Assert.Equal(1, exit);
        }
    }

    [Fact]
    public async Task UrlMode_WithAuthOrConnection_IsRejectedAsMixedMode()
    {
        using var host = new CommandTestHost();
        using (OutputWriter.RedirectTo(new StringWriter()))
        {
            var exit = await new ProfileCreateCliCommand
            {
                Url = "https://contoso.crm.dynamics.com/",
                Auth = "some-cred",
            }.RunAsync();
            Assert.Equal(1, exit);
        }
    }

    [Fact]
    public async Task UrlMode_Headless_FailsWithHeadlessError()
    {
        using var host = new CommandTestHost(headless: true);
        using (OutputWriter.RedirectTo(new StringWriter()))
        {
            var exit = await new ProfileCreateCliCommand
            {
                Url = "https://contoso.crm.dynamics.com/",
            }.RunAsync();
            Assert.Equal(1, exit);
        }

        // No profile was created.
        var profiles = (IProfileStore)host.Provider.GetService(typeof(IProfileStore))!;
        Assert.Null(await profiles.GetAsync("contoso", default));
    }

    [Fact]
    public async Task UrlMode_ExplicitName_OverridesDerived()
    {
        using var host = new CommandTestHost();
        using (OutputWriter.RedirectTo(new StringWriter()))
        {
            var exit = await new ProfileCreateCliCommand
            {
                Url = "https://contoso.crm.dynamics.com/",
                Name = "my-profile",
            }.RunAsync();
            Assert.Equal(0, exit);
        }

        var profiles = (IProfileStore)host.Provider.GetService(typeof(IProfileStore))!;
        var connections = (IConnectionStore)host.Provider.GetService(typeof(IConnectionStore))!;
        Assert.NotNull(await profiles.GetAsync("my-profile", default));
        Assert.NotNull(await connections.GetAsync("my-profile", default));
    }
}
