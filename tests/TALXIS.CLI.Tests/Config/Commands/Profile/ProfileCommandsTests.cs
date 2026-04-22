using System.Text.Json;
using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.Commands.Profile;
using TALXIS.CLI.Config.Model;
using TALXIS.CLI.Shared;
using Xunit;
using ConnectionModel = TALXIS.CLI.Config.Model.Connection;
using ProfileModel = TALXIS.CLI.Config.Model.Profile;

namespace TALXIS.CLI.Tests.Config.Commands.Profile;

[Collection("TxcServicesSerial")]
public sealed class ProfileCommandsTests
{
    private static async Task SeedAsync(CommandTestHost host, string credId = "cred-a", string connId = "conn-a")
    {
        var creds = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;
        var conns = (IConnectionStore)host.Provider.GetService(typeof(IConnectionStore))!;
        await creds.UpsertAsync(new Credential
        {
            Id = credId,
            Kind = CredentialKind.InteractiveBrowser,
            TenantId = "contoso.onmicrosoft.com",
        }, default);
        await conns.UpsertAsync(new ConnectionModel
        {
            Id = connId,
            Provider = ProviderKind.Dataverse,
            EnvironmentUrl = "https://contoso.crm.dynamics.com",
        }, default);
    }

    [Fact]
    public async Task Create_FirstProfile_AutoPromotesToActive()
    {
        using var host = new CommandTestHost();
        await SeedAsync(host);

        var sw = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(sw))
            exit = await new ProfileCreateCliCommand
            {
                Name = "contoso-dev",
                Auth = "cred-a",
                Connection = "conn-a",
                Description = "dev",
            }.RunAsync();

        Assert.Equal(0, exit);

        var global = (IGlobalConfigStore)host.Provider.GetService(typeof(IGlobalConfigStore))!;
        var cfg = await global.LoadAsync(default);
        Assert.Equal("contoso-dev", cfg.ActiveProfile);

        using var doc = JsonDocument.Parse(sw.ToString());
        Assert.True(doc.RootElement.GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task Create_SecondProfile_DoesNotReplaceActivePointer()
    {
        using var host = new CommandTestHost();
        await SeedAsync(host);

        Assert.Equal(0, await new ProfileCreateCliCommand
        { Name = "a", Auth = "cred-a", Connection = "conn-a" }.RunAsync());
        Assert.Equal(0, await new ProfileCreateCliCommand
        { Name = "b", Auth = "cred-a", Connection = "conn-a" }.RunAsync());

        var global = (IGlobalConfigStore)host.Provider.GetService(typeof(IGlobalConfigStore))!;
        Assert.Equal("a", (await global.LoadAsync(default)).ActiveProfile);
    }

    [Fact]
    public async Task Create_ReturnsExit2_WhenAuthMissing()
    {
        using var host = new CommandTestHost();
        await SeedAsync(host);
        var exit = await new ProfileCreateCliCommand
        { Name = "p", Auth = "ghost", Connection = "conn-a" }.RunAsync();
        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task Create_ReturnsExit2_WhenConnectionMissing()
    {
        using var host = new CommandTestHost();
        await SeedAsync(host);
        var exit = await new ProfileCreateCliCommand
        { Name = "p", Auth = "cred-a", Connection = "ghost" }.RunAsync();
        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task List_MarksActiveProfile()
    {
        using var host = new CommandTestHost();
        await SeedAsync(host);
        await new ProfileCreateCliCommand { Name = "a", Auth = "cred-a", Connection = "conn-a" }.RunAsync();
        await new ProfileCreateCliCommand { Name = "b", Auth = "cred-a", Connection = "conn-a" }.RunAsync();

        var sw = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(sw))
            exit = await new ProfileListCliCommand().RunAsync();

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(sw.ToString());
        var arr = doc.RootElement.EnumerateArray().ToList();
        Assert.Equal(2, arr.Count);
        Assert.Single(arr, e => e.GetProperty("active").GetBoolean());
        Assert.True(arr.Single(e => e.GetProperty("id").GetString() == "a").GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task Show_DefaultsToActiveProfile_WhenNameOmitted()
    {
        using var host = new CommandTestHost();
        await SeedAsync(host);
        await new ProfileCreateCliCommand { Name = "active", Auth = "cred-a", Connection = "conn-a" }.RunAsync();

        var sw = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(sw))
            exit = await new ProfileShowCliCommand().RunAsync();

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(sw.ToString());
        Assert.Equal("active", doc.RootElement.GetProperty("id").GetString());
        // Expanded connection + credential should be non-null so scripts don't need a second round-trip.
        Assert.Equal(JsonValueKind.Object, doc.RootElement.GetProperty("connection").ValueKind);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.GetProperty("credential").ValueKind);
    }

    [Fact]
    public async Task Show_ReturnsExit2_WhenNoActiveAndNoName()
    {
        using var host = new CommandTestHost();
        var exit = await new ProfileShowCliCommand().RunAsync();
        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task Show_ReturnsExit2_WhenNamedProfileMissing()
    {
        using var host = new CommandTestHost();
        var exit = await new ProfileShowCliCommand { Name = "ghost" }.RunAsync();
        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task Update_RebindsAuthAndConnection()
    {
        using var host = new CommandTestHost();
        await SeedAsync(host);
        await SeedAsync(host, credId: "cred-b", connId: "conn-b");
        await new ProfileCreateCliCommand { Name = "p", Auth = "cred-a", Connection = "conn-a" }.RunAsync();

        var exit = await new ProfileUpdateCliCommand
        {
            Name = "p",
            Auth = "cred-b",
            Connection = "conn-b",
            Description = "updated",
        }.RunAsync();
        Assert.Equal(0, exit);

        var store = (IProfileStore)host.Provider.GetService(typeof(IProfileStore))!;
        var p = await store.GetAsync("p", default);
        Assert.Equal("cred-b", p!.CredentialRef);
        Assert.Equal("conn-b", p.ConnectionRef);
        Assert.Equal("updated", p.Description);
    }

    [Fact]
    public async Task Update_RefusesNoOp()
    {
        using var host = new CommandTestHost();
        await SeedAsync(host);
        await new ProfileCreateCliCommand { Name = "p", Auth = "cred-a", Connection = "conn-a" }.RunAsync();

        var exit = await new ProfileUpdateCliCommand { Name = "p" }.RunAsync();
        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task Update_ReturnsExit2_WhenProfileMissing()
    {
        using var host = new CommandTestHost();
        var exit = await new ProfileUpdateCliCommand { Name = "ghost", Auth = "x" }.RunAsync();
        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task Update_ReturnsExit2_WhenNewAuthMissing()
    {
        using var host = new CommandTestHost();
        await SeedAsync(host);
        await new ProfileCreateCliCommand { Name = "p", Auth = "cred-a", Connection = "conn-a" }.RunAsync();

        var exit = await new ProfileUpdateCliCommand { Name = "p", Auth = "ghost" }.RunAsync();
        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task Update_EmptyDescription_ClearsField()
    {
        using var host = new CommandTestHost();
        await SeedAsync(host);
        await new ProfileCreateCliCommand
        { Name = "p", Auth = "cred-a", Connection = "conn-a", Description = "initial" }.RunAsync();

        var exit = await new ProfileUpdateCliCommand { Name = "p", Description = "" }.RunAsync();
        Assert.Equal(0, exit);

        var store = (IProfileStore)host.Provider.GetService(typeof(IProfileStore))!;
        var p = await store.GetAsync("p", default);
        Assert.Null(p!.Description);
    }

    [Fact]
    public async Task Select_SetsActivePointer()
    {
        using var host = new CommandTestHost();
        await SeedAsync(host);
        await new ProfileCreateCliCommand { Name = "a", Auth = "cred-a", Connection = "conn-a" }.RunAsync();
        await new ProfileCreateCliCommand { Name = "b", Auth = "cred-a", Connection = "conn-a" }.RunAsync();

        var exit = await new ProfileSelectCliCommand { Name = "b" }.RunAsync();
        Assert.Equal(0, exit);

        var cfg = await ((IGlobalConfigStore)host.Provider.GetService(typeof(IGlobalConfigStore))!).LoadAsync(default);
        Assert.Equal("b", cfg.ActiveProfile);
    }

    [Fact]
    public async Task Select_ReturnsExit2_WhenMissing()
    {
        using var host = new CommandTestHost();
        var exit = await new ProfileSelectCliCommand { Name = "ghost" }.RunAsync();
        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task Delete_ClearsActivePointer_WhenDeletingActive()
    {
        using var host = new CommandTestHost();
        await SeedAsync(host);
        await new ProfileCreateCliCommand { Name = "active", Auth = "cred-a", Connection = "conn-a" }.RunAsync();

        var exit = await new ProfileDeleteCliCommand { Name = "active" }.RunAsync();
        Assert.Equal(0, exit);

        var cfg = await ((IGlobalConfigStore)host.Provider.GetService(typeof(IGlobalConfigStore))!).LoadAsync(default);
        Assert.Null(cfg.ActiveProfile);
    }

    [Fact]
    public async Task Delete_WithoutCascade_KeepsDependents()
    {
        using var host = new CommandTestHost();
        await SeedAsync(host);
        await new ProfileCreateCliCommand { Name = "p", Auth = "cred-a", Connection = "conn-a" }.RunAsync();

        Assert.Equal(0, await new ProfileDeleteCliCommand { Name = "p" }.RunAsync());

        Assert.NotNull(await ((ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!).GetAsync("cred-a", default));
        Assert.NotNull(await ((IConnectionStore)host.Provider.GetService(typeof(IConnectionStore))!).GetAsync("conn-a", default));
    }

    [Fact]
    public async Task Delete_Cascade_RemovesOrphanedDependents()
    {
        using var host = new CommandTestHost();
        await SeedAsync(host);
        await new ProfileCreateCliCommand { Name = "p", Auth = "cred-a", Connection = "conn-a" }.RunAsync();

        Assert.Equal(0, await new ProfileDeleteCliCommand { Name = "p", Cascade = true }.RunAsync());

        Assert.Null(await ((ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!).GetAsync("cred-a", default));
        Assert.Null(await ((IConnectionStore)host.Provider.GetService(typeof(IConnectionStore))!).GetAsync("conn-a", default));
    }

    [Fact]
    public async Task Delete_Cascade_KeepsDependents_StillReferencedByOtherProfile()
    {
        using var host = new CommandTestHost();
        await SeedAsync(host);
        await new ProfileCreateCliCommand { Name = "p1", Auth = "cred-a", Connection = "conn-a" }.RunAsync();
        await new ProfileCreateCliCommand { Name = "p2", Auth = "cred-a", Connection = "conn-a" }.RunAsync();

        Assert.Equal(0, await new ProfileDeleteCliCommand { Name = "p1", Cascade = true }.RunAsync());

        // Dependents are still used by p2 so cascade must keep them.
        Assert.NotNull(await ((ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!).GetAsync("cred-a", default));
        Assert.NotNull(await ((IConnectionStore)host.Provider.GetService(typeof(IConnectionStore))!).GetAsync("conn-a", default));
    }

    [Fact]
    public async Task Delete_ReturnsExit2_WhenMissing()
    {
        using var host = new CommandTestHost();
        var exit = await new ProfileDeleteCliCommand { Name = "ghost" }.RunAsync();
        Assert.Equal(2, exit);
    }
}
