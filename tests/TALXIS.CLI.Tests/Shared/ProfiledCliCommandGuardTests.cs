using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using TALXIS.CLI.Tests.Config.Commands;
using Xunit;
using ConnectionModel = TALXIS.CLI.Core.Model.Connection;

namespace TALXIS.CLI.Tests.Shared;

[Collection("TxcServicesSerial")]
public sealed class ProfiledCliCommandGuardTests
{
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

    private static PowerPlatformEnvironmentSummary CatalogEntry(EnvironmentType? type, string displayName = "Contoso DevBox")
        => new(
            EnvironmentId: Guid.Parse("11111111-2222-3333-4444-555555555555"),
            DisplayName: displayName,
            EnvironmentUrl: new Uri(ProductionGuardTestSupport.EnvUrl + "/"),
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
        await ProductionGuardTestSupport.SeedAsync(host, EnvironmentType.Developer);

        var command = new FakeDestructiveCommand();
        Assert.Equal(0, await command.RunAsync());
        Assert.True(command.Executed);
    }

    [Fact]
    public async Task ProductionType_IsBlocked()
    {
        using var host = new CommandTestHost();
        await ProductionGuardTestSupport.SeedAsync(host, EnvironmentType.Production);

        var command = new FakeDestructiveCommand();
        Assert.Equal(2, await command.RunAsync());
        Assert.False(command.Executed);
    }

    [Fact]
    public async Task ProductionType_AllowProduction_Proceeds()
    {
        using var host = new CommandTestHost();
        await ProductionGuardTestSupport.SeedAsync(host, EnvironmentType.Production);

        var command = new FakeDestructiveCommand { AllowProduction = true };
        Assert.Equal(0, await command.RunAsync());
        Assert.True(command.Executed);
    }

    [Fact]
    public async Task MissingType_IsResolvedFromCatalog_PersistedAndNotBlocked()
    {
        using var host = new CommandTestHost();
        host.EnvironmentCatalog.Add(CatalogEntry(EnvironmentType.Developer));
        await ProductionGuardTestSupport.SeedAsync(host);

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
        await ProductionGuardTestSupport.SeedAsync(host);

        var command = new FakeDestructiveCommand();
        Assert.Equal(2, await command.RunAsync());
        Assert.False(command.Executed);
        Assert.Equal(EnvironmentType.Production, (await StoredConnectionAsync(host)).EnvironmentType);
    }

    [Fact]
    public async Task MissingType_ResolvedButNotPersisted_IsBlocked()
    {
        CommandTestHost.FailingConnectionStore? store = null;
        using var host = new CommandTestHost(decorateConnectionStore: inner => store = new CommandTestHost.FailingConnectionStore(inner));
        host.EnvironmentCatalog.Add(CatalogEntry(EnvironmentType.Developer));
        await ProductionGuardTestSupport.SeedAsync(host);
        store!.FailUpserts = true;

        // The catalog says Developer, but the type could not be recorded, so it
        // stays "unknown" and the guard keeps its fail-safe behaviour.
        var command = new FakeDestructiveCommand();
        Assert.Equal(2, await command.RunAsync());
        Assert.False(command.Executed);
    }

    [Fact]
    public async Task MissingType_CatalogSaysDeveloper_ButNameLooksLikeProduction_IsBlocked()
    {
        using var host = new CommandTestHost();
        host.EnvironmentCatalog.Add(CatalogEntry(EnvironmentType.Developer, displayName: "Contoso Live"));
        await ProductionGuardTestSupport.SeedAsync(host);

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
        await ProductionGuardTestSupport.SeedAsync(host);

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
        await ProductionGuardTestSupport.SeedAsync(host);

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
}
