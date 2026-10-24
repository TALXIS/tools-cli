using System.Text.Json;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Features.Config.Profile;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using Xunit;
using ConnectionModel = TALXIS.CLI.Core.Model.Connection;

namespace TALXIS.CLI.Tests.Config.Commands.Profile;

[Collection("TxcServicesSerial")]
public sealed class ProfileValidateTests
{
    private static async Task SeedAsync(CommandTestHost host)
    {
        var creds = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;
        var conns = (IConnectionStore)host.Provider.GetService(typeof(IConnectionStore))!;
        await creds.UpsertAsync(new Credential
        {
            Id = "cred",
            Kind = CredentialKind.InteractiveBrowser,
            TenantId = "contoso.onmicrosoft.com",
        }, default);
        await conns.UpsertAsync(new ConnectionModel
        {
            Id = "conn",
            Provider = ProviderKind.Dataverse,
            EnvironmentUrl = "https://contoso.crm.dynamics.com",
        }, default);
    }

    [Fact]
    public async Task Validate_ActiveProfile_ReturnsZero_AndInvokesProvider_LiveByDefault()
    {
        using var host = new CommandTestHost();
        await SeedAsync(host);
        await new ProfileCreateCliCommand { Name = "p", Auth = "cred", Connection = "conn" }.RunAsync();

        var exit = await new ProfileValidateCliCommand().RunAsync();
        Assert.Equal(0, exit);
        Assert.Equal(1, host.Provider_Dataverse.Calls);
        Assert.Equal(ValidationMode.Live, host.Provider_Dataverse.LastMode);
    }

    [Fact]
    public async Task Validate_WithSkipLive_PassesStructuralMode()
    {
        using var host = new CommandTestHost();
        await SeedAsync(host);
        await new ProfileCreateCliCommand { Name = "p", Auth = "cred", Connection = "conn" }.RunAsync();

        var exit = await new ProfileValidateCliCommand { SkipLive = true }.RunAsync();
        Assert.Equal(0, exit);
        Assert.Equal(ValidationMode.Structural, host.Provider_Dataverse.LastMode);
    }

    [Fact]
    public async Task Validate_EmitsJsonWithProfileAndStatus()
    {
        using var host = new CommandTestHost();
        await SeedAsync(host);
        await new ProfileCreateCliCommand { Name = "p", Auth = "cred", Connection = "conn" }.RunAsync();

        var sw = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(sw)) { exit = await new ProfileValidateCliCommand { SkipLive = true }.RunAsync(); }
        Assert.Equal(0, exit);

        using var doc = JsonDocument.Parse(sw.ToString());
        Assert.Equal("p", doc.RootElement.GetProperty("profile").GetString());
        Assert.Equal("conn", doc.RootElement.GetProperty("connection").GetString());
        Assert.Equal("cred", doc.RootElement.GetProperty("credential").GetString());
        Assert.Equal("dataverse", doc.RootElement.GetProperty("provider").GetString());
        Assert.Equal("structural", doc.RootElement.GetProperty("mode").GetString());
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Validate_ReturnsExit2_WhenNoActiveAndNoName()
    {
        using var host = new CommandTestHost();
        var exit = await new ProfileValidateCliCommand().RunAsync();
        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task Validate_ReturnsExit2_WhenNamedProfileMissing()
    {
        using var host = new CommandTestHost();
        var exit = await new ProfileValidateCliCommand { Name = "ghost" }.RunAsync();
        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task Validate_ReturnsExit2_WhenConnectionMissing()
    {
        using var host = new CommandTestHost();
        await SeedAsync(host);
        await new ProfileCreateCliCommand { Name = "p", Auth = "cred", Connection = "conn" }.RunAsync();

        // Delete the connection directly to simulate an orphan reference.
        var connStore = (IConnectionStore)host.Provider.GetService(typeof(IConnectionStore))!;
        await connStore.DeleteAsync("conn", default);

        var exit = await new ProfileValidateCliCommand { Name = "p" }.RunAsync();
        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task Validate_ReturnsExit1_WhenProviderThrows()
    {
        using var host = new CommandTestHost();
        host.Provider_Dataverse.Behavior = (_, _, _) => throw new InvalidOperationException("WhoAmI failed: 401");
        await SeedAsync(host);
        await new ProfileCreateCliCommand { Name = "p", Auth = "cred", Connection = "conn" }.RunAsync();

        var exit = await new ProfileValidateCliCommand().RunAsync();
        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task Validate_Named_RespectsArgumentOverActive()
    {
        using var host = new CommandTestHost();
        await SeedAsync(host);
        await new ProfileCreateCliCommand { Name = "a", Auth = "cred", Connection = "conn" }.RunAsync();
        await new ProfileCreateCliCommand { Name = "b", Auth = "cred", Connection = "conn" }.RunAsync();

        var sw = new StringWriter();
        using (OutputWriter.RedirectTo(sw)) { Assert.Equal(0, await new ProfileValidateCliCommand { Name = "b", SkipLive = true }.RunAsync()); }

        using var doc = JsonDocument.Parse(sw.ToString());
        Assert.Equal("b", doc.RootElement.GetProperty("profile").GetString());
    }

    [Fact]
    public async Task Validate_RefreshEnvType_StillRecordsType_WhenLiveCheckFails()
    {
        using var host = new CommandTestHost();
        host.EnvironmentCatalog.Add(new PowerPlatformEnvironmentSummary(
            EnvironmentId: Guid.Parse("11111111-2222-3333-4444-555555555555"),
            DisplayName: "Contoso DevBox",
            EnvironmentUrl: new Uri("https://contoso.crm.dynamics.com/"),
            UniqueName: "contoso-devbox",
            DomainName: "contoso",
            OrganizationId: Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            EnvironmentType: EnvironmentType.Developer));
        host.Provider_Dataverse.Behavior = (_, _, _) => throw new HttpRequestException("host unreachable");
        await SeedAsync(host);
        await new ProfileCreateCliCommand { Name = "p", Auth = "cred", Connection = "conn" }.RunAsync();

        var exit = await new ProfileValidateCliCommand { RefreshEnvType = true }.RunAsync();

        // The live check fails, but the catalog refresh already ran and persisted the type.
        Assert.Equal(1, exit);
        var conns = (IConnectionStore)host.Provider.GetService(typeof(IConnectionStore))!;
        Assert.Equal(EnvironmentType.Developer, (await conns.GetAsync("conn", default))!.EnvironmentType);
    }
}
