using System.Text.Json;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Features.Config.Connection;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core;
using Xunit;
using ConnectionModel = TALXIS.CLI.Core.Model.Connection;
using ProfileModel = TALXIS.CLI.Core.Model.Profile;

namespace TALXIS.CLI.Tests.Config.Commands.Connection;

[Collection("TxcServicesSerial")]
public sealed class ConnectionCommandsTests
{
    [Fact]
    public async Task Create_Persists_DataverseConnection_AndEchoesJson()
    {
        using var host = new CommandTestHost();

        var sw = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(sw))
            exit = await new ConnectionCreateCliCommand
            {
                Name = "contoso-dev",
                Provider = ProviderKind.Dataverse,
                EnvironmentUrl = "https://contoso-dev.crm.dynamics.com/",
                Cloud = CloudInstance.Public,
                OrganizationId = "11111111-1111-1111-1111-111111111111",
                TenantId = "contoso.onmicrosoft.com",
                Description = "Dev",
            }.RunAsync();

        Assert.Equal(0, exit);

        var store = (IConnectionStore)host.Provider.GetService(typeof(IConnectionStore))!;
        var conn = await store.GetAsync("contoso-dev", default);
        Assert.NotNull(conn);
        Assert.Equal(ProviderKind.Dataverse, conn!.Provider);
        Assert.Equal("https://contoso-dev.crm.dynamics.com", conn.EnvironmentUrl);
        Assert.Equal(CloudInstance.Public, conn.Cloud);
        Assert.Equal("11111111-1111-1111-1111-111111111111", conn.OrganizationId);
        Assert.Equal("contoso.onmicrosoft.com", conn.TenantId);

        using var doc = JsonDocument.Parse(sw.ToString());
        Assert.Equal("contoso-dev", doc.RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Create_RejectsNonDataverseProvider_InV1()
    {
        using var host = new CommandTestHost();
        var exit = await new ConnectionCreateCliCommand
        {
            Name = "az",
            Provider = ProviderKind.Azure,
            EnvironmentUrl = "https://anything/",
        }.RunAsync();
        Assert.Equal(1, exit);

        var store = (IConnectionStore)host.Provider.GetService(typeof(IConnectionStore))!;
        Assert.Empty(await store.ListAsync(default));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com")]
    public async Task Create_Rejects_MissingOrInvalidEnvironmentUrl(string? url)
    {
        using var host = new CommandTestHost();
        var exit = await new ConnectionCreateCliCommand
        {
            Name = "bad",
            Provider = ProviderKind.Dataverse,
            EnvironmentUrl = url,
        }.RunAsync();
        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task Create_Rejects_NonGuid_OrganizationId()
    {
        using var host = new CommandTestHost();
        var exit = await new ConnectionCreateCliCommand
        {
            Name = "org-bad",
            Provider = ProviderKind.Dataverse,
            EnvironmentUrl = "https://contoso.crm.dynamics.com",
            OrganizationId = "not-a-guid",
        }.RunAsync();
        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task List_EmitsJsonArray_OfStoredConnections()
    {
        using var host = new CommandTestHost();
        var store = (IConnectionStore)host.Provider.GetService(typeof(IConnectionStore))!;
        await store.UpsertAsync(new ConnectionModel
        {
            Id = "a",
            Provider = ProviderKind.Dataverse,
            EnvironmentUrl = "https://a.crm.dynamics.com",
        }, default);
        await store.UpsertAsync(new ConnectionModel
        {
            Id = "b",
            Provider = ProviderKind.Dataverse,
            EnvironmentUrl = "https://b.crm.dynamics.com",
        }, default);

        var sw = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(sw))
            exit = await new ConnectionListCliCommand().RunAsync();
        Assert.Equal(0, exit);

        using var doc = JsonDocument.Parse(sw.ToString());
        Assert.Equal(2, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Show_ReturnsExit2_WhenMissing()
    {
        using var host = new CommandTestHost();
        var exit = await new ConnectionGetCliCommand { Name = "ghost" }.RunAsync();
        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task Show_EmitsJson_WhenFound()
    {
        using var host = new CommandTestHost();
        var store = (IConnectionStore)host.Provider.GetService(typeof(IConnectionStore))!;
        await store.UpsertAsync(new ConnectionModel
        {
            Id = "only",
            Provider = ProviderKind.Dataverse,
            EnvironmentUrl = "https://only.crm.dynamics.com",
        }, default);

        var sw = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(sw))
            exit = await new ConnectionGetCliCommand { Name = "only" }.RunAsync();
        Assert.Equal(0, exit);

        using var doc = JsonDocument.Parse(sw.ToString());
        Assert.Equal("only", doc.RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Delete_FailsWithExit3_WhenProfilesReference_AndNoForceFlag()
    {
        using var host = new CommandTestHost();
        var connStore = (IConnectionStore)host.Provider.GetService(typeof(IConnectionStore))!;
        var profStore = (IProfileStore)host.Provider.GetService(typeof(IProfileStore))!;
        await connStore.UpsertAsync(new ConnectionModel
        {
            Id = "c1",
            Provider = ProviderKind.Dataverse,
            EnvironmentUrl = "https://c1.crm.dynamics.com",
        }, default);
        await profStore.UpsertAsync(new ProfileModel
        {
            Id = "p1",
            ConnectionRef = "c1",
            CredentialRef = "whatever",
        }, default);

        var exit = await new ConnectionDeleteCliCommand { Name = "c1" }.RunAsync();
        Assert.Equal(2, exit);
        Assert.NotNull(await connStore.GetAsync("c1", default));
    }

    [Fact]
    public async Task Delete_OrphansProfiles_WhenForceFlagIsSet()
    {
        using var host = new CommandTestHost();
        var connStore = (IConnectionStore)host.Provider.GetService(typeof(IConnectionStore))!;
        var profStore = (IProfileStore)host.Provider.GetService(typeof(IProfileStore))!;
        await connStore.UpsertAsync(new ConnectionModel
        {
            Id = "c1",
            Provider = ProviderKind.Dataverse,
            EnvironmentUrl = "https://c1.crm.dynamics.com",
        }, default);
        await profStore.UpsertAsync(new ProfileModel
        {
            Id = "p1",
            ConnectionRef = "c1",
            CredentialRef = "whatever",
        }, default);

        var exit = await new ConnectionDeleteCliCommand
        {
            Name = "c1",
            ForceOrphanProfiles = true,
        }.RunAsync();
        Assert.Equal(0, exit);
        Assert.Null(await connStore.GetAsync("c1", default));

        // The orphaned profile is intentionally preserved — pac-auth-clear parity.
        var p = await profStore.GetAsync("p1", default);
        Assert.NotNull(p);
        Assert.Equal("c1", p!.ConnectionRef);
    }

    [Fact]
    public async Task Delete_ReturnsExit2_WhenMissing()
    {
        using var host = new CommandTestHost();
        var exit = await new ConnectionDeleteCliCommand { Name = "ghost" }.RunAsync();
        Assert.Equal(2, exit);
    }
}
