using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Features.Config.Profile;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using Xunit;
using ConnectionModel = TALXIS.CLI.Core.Model.Connection;

namespace TALXIS.CLI.Tests.Config.Commands;

[Collection("TxcServicesSerial")]
public sealed class ProductionGuardTests
{
    private const string EnvUrl = "https://devbox.crm.dynamics.com";

    [CliDestructive("Removes something from the target environment.")]
    private sealed class FakeDestructiveCommand : ProfiledCliCommand, IDestructiveCommand
    {
        public bool Yes { get; set; } = true;
        public bool Executed { get; private set; }

        protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(FakeDestructiveCommand));

        protected override Task<int> ExecuteAsync()
        {
            Executed = true;
            return Task.FromResult(ExitSuccess);
        }
    }

    private static async Task SeedAsync(CommandTestHost host, EnvironmentType? type = null, string? displayName = null)
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
            EnvironmentUrl = EnvUrl,
            EnvironmentType = type,
            DisplayName = displayName,
        }, default);
        Assert.Equal(0, await new ProfileCreateCliCommand { Name = "p", Auth = "cred", Connection = "conn" }.RunAsync());
    }

    private static PowerPlatformEnvironmentSummary CatalogEntry(EnvironmentType? type, string displayName = "Contoso DevBox")
        => new(
            EnvironmentId: Guid.Parse("11111111-2222-3333-4444-555555555555"),
            DisplayName: displayName,
            EnvironmentUrl: new Uri(EnvUrl + "/"),
            UniqueName: "contoso-devbox",
            DomainName: "devbox",
            OrganizationId: Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            EnvironmentType: type);

    private static async Task<ConnectionModel> StoredConnectionAsync(CommandTestHost host)
    {
        var conns = (IConnectionStore)host.Provider.GetService(typeof(IConnectionStore))!;
        return (await conns.GetAsync("conn", default))!;
    }

    [Fact]
    public async Task KnownNonProductionType_IsNotBlocked_AndNeverQueriesCatalog()
    {
        using var host = new CommandTestHost();
        // A catalog failure would surface if the guard queried it needlessly.
        host.EnvironmentCatalog.Failure = new HttpRequestException("catalog must not be called");
        await SeedAsync(host, EnvironmentType.Developer);

        var command = new FakeDestructiveCommand();
        Assert.Equal(0, await command.RunAsync());
        Assert.True(command.Executed);
    }

    [Fact]
    public async Task ProductionType_IsBlocked()
    {
        using var host = new CommandTestHost();
        await SeedAsync(host, EnvironmentType.Production);

        var command = new FakeDestructiveCommand();
        Assert.Equal(2, await command.RunAsync());
        Assert.False(command.Executed);
    }

    [Fact]
    public async Task ProductionType_AllowProduction_Proceeds()
    {
        using var host = new CommandTestHost();
        await SeedAsync(host, EnvironmentType.Production);

        var command = new FakeDestructiveCommand { AllowProduction = true };
        Assert.Equal(0, await command.RunAsync());
        Assert.True(command.Executed);
    }

    [Fact]
    public async Task MissingType_IsResolvedFromCatalog_PersistedAndNotBlocked()
    {
        using var host = new CommandTestHost();
        host.EnvironmentCatalog.Add(CatalogEntry(EnvironmentType.Developer));
        await SeedAsync(host);

        var command = new FakeDestructiveCommand();
        Assert.Equal(0, await command.RunAsync());
        Assert.True(command.Executed);

        var stored = await StoredConnectionAsync(host);
        Assert.Equal(EnvironmentType.Developer, stored.EnvironmentType);
        Assert.Equal("Contoso DevBox", stored.DisplayName);
    }

    [Fact]
    public async Task MissingType_CatalogSaysProduction_IsBlocked()
    {
        using var host = new CommandTestHost();
        host.EnvironmentCatalog.Add(CatalogEntry(EnvironmentType.Production));
        await SeedAsync(host);

        var command = new FakeDestructiveCommand();
        Assert.Equal(2, await command.RunAsync());
        Assert.False(command.Executed);
        Assert.Equal(EnvironmentType.Production, (await StoredConnectionAsync(host)).EnvironmentType);
    }

    [Fact]
    public async Task MissingType_CatalogSaysDeveloper_ButNameLooksLikeProduction_IsBlocked()
    {
        using var host = new CommandTestHost();
        host.EnvironmentCatalog.Add(CatalogEntry(EnvironmentType.Developer, displayName: "Contoso Live"));
        await SeedAsync(host);

        var command = new FakeDestructiveCommand();
        Assert.Equal(2, await command.RunAsync());
        Assert.False(command.Executed);
    }

    [Theory]
    [InlineData("network")]
    [InlineData("missing")]
    [InlineData("unknownSku")]
    public async Task MissingType_CannotBeResolved_StaysBlocked(string cause)
    {
        using var host = new CommandTestHost();
        switch (cause)
        {
            case "network":
                host.EnvironmentCatalog.Failure = new HttpRequestException("proxy 502");
                break;
            case "unknownSku":
                host.EnvironmentCatalog.Add(CatalogEntry(type: null));
                break;
            // "missing": environment is simply not in the catalog.
        }
        await SeedAsync(host);

        var command = new FakeDestructiveCommand();
        Assert.Equal(2, await command.RunAsync());
        Assert.False(command.Executed);
        Assert.Null((await StoredConnectionAsync(host)).EnvironmentType);
    }

    [Fact]
    public async Task MissingType_CannotBeResolved_AllowProduction_Proceeds()
    {
        using var host = new CommandTestHost();
        host.EnvironmentCatalog.Failure = new HttpRequestException("proxy 502");
        await SeedAsync(host);

        var command = new FakeDestructiveCommand { AllowProduction = true };
        Assert.Equal(0, await command.RunAsync());
        Assert.True(command.Executed);
    }

    [Fact]
    public async Task UnresolvableProfile_BlocksDestructiveCommand_EvenWithYes()
    {
        using var host = new CommandTestHost();

        var command = new FakeDestructiveCommand { Profile = "ghost", Yes = true };
        Assert.Equal(2, await command.RunAsync());
        Assert.False(command.Executed);
    }

    [Fact]
    public async Task ValidateRefreshEnvType_StillRecordsType_WhenLiveCheckFails()
    {
        using var host = new CommandTestHost();
        host.EnvironmentCatalog.Add(CatalogEntry(EnvironmentType.Developer));
        host.Provider_Dataverse.Behavior = (_, _, _) => throw new HttpRequestException("host unreachable");
        await SeedAsync(host);

        var exit = await new ProfileValidateCliCommand { RefreshEnvType = true }.RunAsync();

        Assert.Equal(1, exit);
        Assert.Equal(EnvironmentType.Developer, (await StoredConnectionAsync(host)).EnvironmentType);
    }
}
