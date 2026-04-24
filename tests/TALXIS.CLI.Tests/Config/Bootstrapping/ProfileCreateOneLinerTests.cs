using System.Text.Json;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Features.Config.Profile;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Headless;
using TALXIS.CLI.Core;
using TALXIS.CLI.Platform.Dataverse.PowerPlatform;
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
        Assert.True(doc.RootElement.GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task UrlMode_ReplacesExistingActiveProfile_ByDefault()
    {
        using var host = new CommandTestHost();

        var global = (IGlobalConfigStore)host.Provider.GetService(typeof(IGlobalConfigStore))!;
        await global.SaveAsync(new GlobalConfig { ActiveProfile = "existing" }, default);

        using var sw = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(sw))
            exit = await new ProfileCreateCliCommand
            {
                Url = "https://contoso.crm4.dynamics.com/",
            }.RunAsync();

        Assert.Equal(0, exit);
        Assert.Equal("contoso", (await global.LoadAsync(default)).ActiveProfile);

        using var doc = JsonDocument.Parse(sw.ToString());
        Assert.True(doc.RootElement.GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task UrlMode_NoSelect_KeepsExistingActiveProfile()
    {
        using var host = new CommandTestHost();

        var global = (IGlobalConfigStore)host.Provider.GetService(typeof(IGlobalConfigStore))!;
        await global.SaveAsync(new GlobalConfig { ActiveProfile = "existing" }, default);

        using var sw = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(sw))
            exit = await new ProfileCreateCliCommand
            {
                Url = "https://contoso.crm4.dynamics.com/",
                NoSelect = true,
            }.RunAsync();

        Assert.Equal(0, exit);
        Assert.Equal("existing", (await global.LoadAsync(default)).ActiveProfile);

        using var doc = JsonDocument.Parse(sw.ToString());
        Assert.False(doc.RootElement.GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task UrlMode_UsesEnvironmentDisplayName_AndHostForDerivedName()
    {
        var catalog = new CommandTestHost.FakePowerPlatformEnvironmentCatalog();
        catalog.Add(new PowerPlatformEnvironmentSummary(
            EnvironmentId: Guid.Parse("11111111-2222-3333-4444-555555555555"),
            DisplayName: "Contoso Dev",
            EnvironmentUrl: new Uri("https://org0fadb1dd.crm.dynamics.com/"),
            UniqueName: "contoso-dev",
            DomainName: "org0fadb1dd",
            OrganizationId: Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")));

        using var host = new CommandTestHost(environmentCatalog: catalog);
        using (OutputWriter.RedirectTo(new StringWriter()))
        {
            var exit = await new ProfileCreateCliCommand
            {
                Url = "https://org0fadb1dd.crm.dynamics.com/",
            }.RunAsync();
            Assert.Equal(0, exit);
        }

        var profiles = (IProfileStore)host.Provider.GetService(typeof(IProfileStore))!;
        var connections = (IConnectionStore)host.Provider.GetService(typeof(IConnectionStore))!;
        Assert.NotNull(await profiles.GetAsync("contoso-dev-org0fadb1dd", default));
        Assert.NotNull(await connections.GetAsync("contoso-dev-org0fadb1dd", default));
    }

    [Fact]
    public async Task UrlMode_DerivedName_CollidesWithExistingConnection_PicksSuffix()
    {
        var catalog = new CommandTestHost.FakePowerPlatformEnvironmentCatalog();
        catalog.Add(new PowerPlatformEnvironmentSummary(
            EnvironmentId: Guid.Parse("11111111-2222-3333-4444-555555555555"),
            DisplayName: "Contoso Dev",
            EnvironmentUrl: new Uri("https://org0fadb1dd.crm.dynamics.com/"),
            UniqueName: "contoso-dev",
            DomainName: "org0fadb1dd",
            OrganizationId: Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")));

        using var host = new CommandTestHost(environmentCatalog: catalog);
        var connections = (IConnectionStore)host.Provider.GetService(typeof(IConnectionStore))!;
        await connections.UpsertAsync(new Connection { Id = "contoso-dev-org0fadb1dd", Provider = ProviderKind.Dataverse, EnvironmentUrl = "https://existing" }, default);

        using (OutputWriter.RedirectTo(new StringWriter()))
        {
            var exit = await new ProfileCreateCliCommand
            {
                Url = "https://org0fadb1dd.crm.dynamics.com/",
            }.RunAsync();
            Assert.Equal(0, exit);
        }

        var profiles = (IProfileStore)host.Provider.GetService(typeof(IProfileStore))!;
        Assert.NotNull(await profiles.GetAsync("contoso-dev-org0fadb1dd-2", default));
    }

    [Fact]
    public async Task UrlMode_WhenEnvironmentLookupFails_FallsBackToHostName()
    {
        var catalog = new CommandTestHost.FakePowerPlatformEnvironmentCatalog
        {
            Failure = new InvalidOperationException("admin lookup failed"),
        };

        using var host = new CommandTestHost(environmentCatalog: catalog);
        using (OutputWriter.RedirectTo(new StringWriter()))
        {
            var exit = await new ProfileCreateCliCommand
            {
                Url = "https://fallback.crm.dynamics.com/",
            }.RunAsync();
            Assert.Equal(0, exit);
        }

        var profiles = (IProfileStore)host.Provider.GetService(typeof(IProfileStore))!;
        Assert.NotNull(await profiles.GetAsync("fallback", default));
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
    public async Task NoSelect_WithoutUrl_IsRejected()
    {
        using var host = new CommandTestHost();
        using (OutputWriter.RedirectTo(new StringWriter()))
        {
            var exit = await new ProfileCreateCliCommand
            {
                Auth = "some-cred",
                Connection = "some-conn",
                NoSelect = true,
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

    [Fact]
    public async Task UrlMode_ReusesSameInteractiveCredential_AcrossEnvironments()
    {
        using var host = new CommandTestHost();
        using (OutputWriter.RedirectTo(new StringWriter()))
        {
            Assert.Equal(0, await new ProfileCreateCliCommand
            {
                Url = "https://contoso.crm.dynamics.com/",
            }.RunAsync());
            Assert.Equal(0, await new ProfileCreateCliCommand
            {
                Url = "https://fabrikam.crm.dynamics.com/",
            }.RunAsync());
        }

        var profiles = (IProfileStore)host.Provider.GetService(typeof(IProfileStore))!;
        var creds = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;

        var contoso = await profiles.GetAsync("contoso", default);
        var fabrikam = await profiles.GetAsync("fabrikam", default);
        Assert.NotNull(contoso);
        Assert.NotNull(fabrikam);

        var credentials = await creds.ListAsync(default);
        var credential = Assert.Single(credentials);
        Assert.Equal(contoso!.CredentialRef, fabrikam!.CredentialRef);
        Assert.Equal(credential.Id, contoso.CredentialRef);
    }

    [Fact]
    public async Task UrlMode_SameEnvironment_AndSameAccount_DoesNotDuplicateProfileConnectionOrCredential()
    {
        using var host = new CommandTestHost();
        using (OutputWriter.RedirectTo(new StringWriter()))
        {
            Assert.Equal(0, await new ProfileCreateCliCommand
            {
                Url = "https://contoso.crm.dynamics.com/",
            }.RunAsync());
            Assert.Equal(0, await new ProfileCreateCliCommand
            {
                Url = "https://contoso.crm.dynamics.com/",
            }.RunAsync());
        }

        var profiles = (IProfileStore)host.Provider.GetService(typeof(IProfileStore))!;
        var connections = (IConnectionStore)host.Provider.GetService(typeof(IConnectionStore))!;
        var creds = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;
        var global = (IGlobalConfigStore)host.Provider.GetService(typeof(IGlobalConfigStore))!;

        var profileList = await profiles.ListAsync(default);
        var connectionList = await connections.ListAsync(default);
        var credentialList = await creds.ListAsync(default);

        var profile = Assert.Single(profileList);
        var connection = Assert.Single(connectionList);
        var credential = Assert.Single(credentialList);

        Assert.Equal("contoso", profile.Id);
        Assert.Equal("contoso", profile.ConnectionRef);
        Assert.Equal(connection.Id, profile.ConnectionRef);
        Assert.Equal(credential.Id, profile.CredentialRef);
        Assert.Equal("contoso", (await global.LoadAsync(default)).ActiveProfile);
    }

    [Fact]
    public async Task UrlMode_SameEnvironment_WithExplicitDifferentProfileName_ReusesConnectionAndCredential()
    {
        using var host = new CommandTestHost();
        using (OutputWriter.RedirectTo(new StringWriter()))
        {
            Assert.Equal(0, await new ProfileCreateCliCommand
            {
                Url = "https://contoso.crm.dynamics.com/",
            }.RunAsync());
            Assert.Equal(0, await new ProfileCreateCliCommand
            {
                Url = "https://contoso.crm.dynamics.com/",
                Name = "contoso-second",
            }.RunAsync());
        }

        var profiles = (IProfileStore)host.Provider.GetService(typeof(IProfileStore))!;
        var connections = (IConnectionStore)host.Provider.GetService(typeof(IConnectionStore))!;
        var creds = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;

        var profileList = await profiles.ListAsync(default);
        var connection = Assert.Single(await connections.ListAsync(default));
        var credential = Assert.Single(await creds.ListAsync(default));

        Assert.Equal(2, profileList.Count);
        Assert.All(profileList, profile => Assert.Equal(connection.Id, profile.ConnectionRef));
        Assert.All(profileList, profile => Assert.Equal(credential.Id, profile.CredentialRef));
        Assert.Contains(profileList, profile => profile.Id == "contoso");
        Assert.Contains(profileList, profile => profile.Id == "contoso-second");
    }
}
