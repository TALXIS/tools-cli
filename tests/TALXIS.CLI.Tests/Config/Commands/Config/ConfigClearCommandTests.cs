using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Resolution;
using TALXIS.CLI.Core.Storage;
using TALXIS.CLI.Features.Config;
using TALXIS.CLI.Features.Config.Profile;
using TALXIS.CLI.Tests.Config.Commands;
using Xunit;
using ConnectionModel = TALXIS.CLI.Core.Model.Connection;

namespace TALXIS.CLI.Tests.Config.Commands.Config;

[Collection("TxcServicesSerial")]
public sealed class ConfigClearCommandTests : IDisposable
{
    private readonly string _cwd;

    public ConfigClearCommandTests()
    {
        _cwd = Path.Combine(Path.GetTempPath(), "txc-clear-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cwd);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_cwd)) Directory.Delete(_cwd, recursive: true); }
        catch { /* best effort */ }
    }

    private static async Task SeedProfileAsync(CommandTestHost host, string profileName = "active")
    {
        var creds = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;
        var conns = (IConnectionStore)host.Provider.GetService(typeof(IConnectionStore))!;
        await creds.UpsertAsync(new Credential
        {
            Id = "cred",
            Kind = CredentialKind.ClientSecret,
            SecretRef = SecretRef.Create("cred", "client-secret"),
            TenantId = "contoso.onmicrosoft.com",
        }, default);
        await conns.UpsertAsync(new ConnectionModel
        {
            Id = "conn",
            Provider = ProviderKind.Dataverse,
            EnvironmentUrl = "https://contoso.crm.dynamics.com",
        }, default);

        await host.Vault.SetSecretAsync(SecretRef.Create("cred", "client-secret"), "super-secret", default);
        await new ProfileCreateCliCommand { Name = profileName, Auth = "cred", Connection = "conn" }.RunAsync();
    }

    [Fact]
    public async Task Clear_RemovesGlobalState_AndCurrentWorkspacePin()
    {
        using var host = new CommandTestHost(currentDirectory: _cwd);
        await SeedProfileAsync(host);
        Assert.Equal(0, await new ProfilePinCliCommand().RunAsync());

        Directory.CreateDirectory(host.Temp.Paths.AuthDirectory);
        await File.WriteAllTextAsync(Path.Combine(host.Temp.Paths.AuthDirectory, "marker.dat"), "x");

        var exit = await new ConfigClearCliCommand().RunAsync();
        Assert.Equal(0, exit);

        Assert.False(Directory.Exists(host.Temp.Path));
        Assert.Empty(host.Vault.Contents);

        var workspaceFile = Path.Combine(_cwd, WorkspaceDiscovery.DirectoryName, WorkspaceDiscovery.FileName);
        Assert.False(File.Exists(workspaceFile));
        Assert.False(Directory.Exists(Path.Combine(_cwd, WorkspaceDiscovery.DirectoryName)));

        var profiles = (IProfileStore)host.Provider.GetService(typeof(IProfileStore))!;
        var connections = (IConnectionStore)host.Provider.GetService(typeof(IConnectionStore))!;
        var credentials = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;
        Assert.Empty(await profiles.ListAsync(default));
        Assert.Empty(await connections.ListAsync(default));
        Assert.Empty(await credentials.ListAsync(default));
    }

    [Fact]
    public async Task Clear_IsIdempotent_WhenNoStateExists()
    {
        using var host = new CommandTestHost(currentDirectory: _cwd);

        Assert.Equal(0, await new ConfigClearCliCommand().RunAsync());
        Assert.Equal(0, await new ConfigClearCliCommand().RunAsync());
    }

    [Fact]
    public async Task Clear_RemovesWorkspacePin_ButKeepsSiblingFiles()
    {
        using var host = new CommandTestHost(currentDirectory: _cwd);
        await SeedProfileAsync(host);
        Assert.Equal(0, await new ProfilePinCliCommand().RunAsync());

        var workspaceDir = Path.Combine(_cwd, WorkspaceDiscovery.DirectoryName);
        var sibling = Path.Combine(workspaceDir, "other.json");
        await File.WriteAllTextAsync(sibling, "{}");

        Assert.Equal(0, await new ConfigClearCliCommand().RunAsync());

        Assert.True(Directory.Exists(workspaceDir));
        Assert.True(File.Exists(sibling));
        Assert.False(File.Exists(Path.Combine(workspaceDir, WorkspaceDiscovery.FileName)));
    }
}
