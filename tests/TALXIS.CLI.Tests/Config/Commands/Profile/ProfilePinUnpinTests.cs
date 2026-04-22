using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.Commands.Profile;
using TALXIS.CLI.Config.Model;
using TALXIS.CLI.Config.Resolution;
using TALXIS.CLI.Config.Storage;
using Xunit;
using ConnectionModel = TALXIS.CLI.Config.Model.Connection;

namespace TALXIS.CLI.Tests.Config.Commands.Profile;

[Collection("TxcServicesSerial")]
public sealed class ProfilePinUnpinTests : IDisposable
{
    private readonly string _cwd;

    public ProfilePinUnpinTests()
    {
        // Isolated scratch cwd so the pin file never pollutes the real repo root.
        _cwd = Path.Combine(Path.GetTempPath(), "txc-pin-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cwd);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_cwd)) Directory.Delete(_cwd, recursive: true); }
        catch { /* best effort */ }
    }

    private async Task SeedAsync(CommandTestHost host)
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
    public async Task Pin_WritesWorkspaceFile_ForActiveProfile()
    {
        using var host = new CommandTestHost(currentDirectory: _cwd);
        await SeedAsync(host);
        await new ProfileCreateCliCommand { Name = "active", Auth = "cred", Connection = "conn" }.RunAsync();

        var exit = await new ProfilePinCliCommand().RunAsync();
        Assert.Equal(0, exit);

        var file = Path.Combine(_cwd, WorkspaceDiscovery.DirectoryName, WorkspaceDiscovery.FileName);
        Assert.True(File.Exists(file));
        var wc = await JsonFile.ReadOrDefaultAsync<WorkspaceConfig>(file, default);
        Assert.Equal("active", wc.DefaultProfile);
    }

    [Fact]
    public async Task Pin_WithName_PinsSpecificProfile()
    {
        using var host = new CommandTestHost(currentDirectory: _cwd);
        await SeedAsync(host);
        await new ProfileCreateCliCommand { Name = "a", Auth = "cred", Connection = "conn" }.RunAsync();
        await new ProfileCreateCliCommand { Name = "b", Auth = "cred", Connection = "conn" }.RunAsync();

        Assert.Equal(0, await new ProfilePinCliCommand { Name = "b" }.RunAsync());

        var file = Path.Combine(_cwd, WorkspaceDiscovery.DirectoryName, WorkspaceDiscovery.FileName);
        var wc = await JsonFile.ReadOrDefaultAsync<WorkspaceConfig>(file, default);
        Assert.Equal("b", wc.DefaultProfile);
    }

    [Fact]
    public async Task Pin_ReturnsExit2_WhenNoActiveAndNoName()
    {
        using var host = new CommandTestHost(currentDirectory: _cwd);
        var exit = await new ProfilePinCliCommand().RunAsync();
        Assert.Equal(2, exit);
        Assert.False(File.Exists(Path.Combine(_cwd, WorkspaceDiscovery.DirectoryName, WorkspaceDiscovery.FileName)));
    }

    [Fact]
    public async Task Pin_ReturnsExit2_WhenNamedProfileMissing()
    {
        using var host = new CommandTestHost(currentDirectory: _cwd);
        var exit = await new ProfilePinCliCommand { Name = "ghost" }.RunAsync();
        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task Pin_IsIdempotent_OverwritesExistingFile()
    {
        using var host = new CommandTestHost(currentDirectory: _cwd);
        await SeedAsync(host);
        await new ProfileCreateCliCommand { Name = "a", Auth = "cred", Connection = "conn" }.RunAsync();
        await new ProfileCreateCliCommand { Name = "b", Auth = "cred", Connection = "conn" }.RunAsync();

        Assert.Equal(0, await new ProfilePinCliCommand { Name = "a" }.RunAsync());
        Assert.Equal(0, await new ProfilePinCliCommand { Name = "b" }.RunAsync());

        var file = Path.Combine(_cwd, WorkspaceDiscovery.DirectoryName, WorkspaceDiscovery.FileName);
        var wc = await JsonFile.ReadOrDefaultAsync<WorkspaceConfig>(file, default);
        Assert.Equal("b", wc.DefaultProfile);
    }

    [Fact]
    public async Task Unpin_RemovesWorkspaceFile_AndEmptyDirectory()
    {
        using var host = new CommandTestHost(currentDirectory: _cwd);
        await SeedAsync(host);
        await new ProfileCreateCliCommand { Name = "p", Auth = "cred", Connection = "conn" }.RunAsync();
        Assert.Equal(0, await new ProfilePinCliCommand().RunAsync());

        var exit = await new ProfileUnpinCliCommand().RunAsync();
        Assert.Equal(0, exit);

        var dir = Path.Combine(_cwd, WorkspaceDiscovery.DirectoryName);
        Assert.False(Directory.Exists(dir), "empty .txc/ should be removed too");
    }

    [Fact]
    public async Task Unpin_IsIdempotent_WhenNoPinExists()
    {
        using var host = new CommandTestHost(currentDirectory: _cwd);
        var exit = await new ProfileUnpinCliCommand().RunAsync();
        Assert.Equal(0, exit);
    }

    [Fact]
    public async Task Unpin_KeepsSiblingFiles_InTxcDir()
    {
        using var host = new CommandTestHost(currentDirectory: _cwd);
        await SeedAsync(host);
        await new ProfileCreateCliCommand { Name = "p", Auth = "cred", Connection = "conn" }.RunAsync();
        Assert.Equal(0, await new ProfilePinCliCommand().RunAsync());

        var dir = Path.Combine(_cwd, WorkspaceDiscovery.DirectoryName);
        var sibling = Path.Combine(dir, "other.json");
        await File.WriteAllTextAsync(sibling, "{}");

        Assert.Equal(0, await new ProfileUnpinCliCommand().RunAsync());

        // Directory must survive because it still has other content.
        Assert.True(Directory.Exists(dir));
        Assert.True(File.Exists(sibling));
    }
}
