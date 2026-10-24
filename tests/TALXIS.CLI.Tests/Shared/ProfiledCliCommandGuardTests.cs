using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Logging;
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
        public TimeSpan LookupTimeout { get; init; } = TimeSpan.FromSeconds(20);

        protected override TimeSpan EnvironmentTypeLookupTimeout => LookupTimeout;

        protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(FakeDestructiveCommand));

        protected override Task<int> ExecuteAsync()
        {
            Executed = true;
            return Task.FromResult(ExitSuccess);
        }
    }

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
        await host.SeedActiveProfileAsync(EnvironmentType.Developer);

        var command = new FakeDestructiveCommand();
        Assert.Equal(0, await command.RunAsync());
        Assert.True(command.Executed);
    }

    [Fact]
    public async Task ProductionType_IsBlocked()
    {
        using var host = new CommandTestHost();
        await host.SeedActiveProfileAsync(EnvironmentType.Production);

        var command = new FakeDestructiveCommand();
        Assert.Equal(2, await command.RunAsync());
        Assert.False(command.Executed);
    }

    [Fact]
    public async Task ProductionType_AllowProduction_Proceeds()
    {
        using var host = new CommandTestHost();
        await host.SeedActiveProfileAsync(EnvironmentType.Production);

        var command = new FakeDestructiveCommand { AllowProduction = true };
        Assert.Equal(0, await command.RunAsync());
        Assert.True(command.Executed);
    }

    [Fact]
    public async Task MissingType_IsResolvedFromCatalog_PersistedAndNotBlocked()
    {
        using var host = new CommandTestHost();
        host.EnvironmentCatalog.AddEnvironment(CommandTestHost.DefaultEnvironmentUrl, EnvironmentType.Developer);
        await host.SeedActiveProfileAsync();

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
        host.EnvironmentCatalog.AddEnvironment(CommandTestHost.DefaultEnvironmentUrl, EnvironmentType.Production);
        await host.SeedActiveProfileAsync();

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
        host.EnvironmentCatalog.AddEnvironment(CommandTestHost.DefaultEnvironmentUrl, EnvironmentType.Developer);
        await host.SeedActiveProfileAsync();
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
        host.EnvironmentCatalog.AddEnvironment(CommandTestHost.DefaultEnvironmentUrl, EnvironmentType.Developer, displayName: "Contoso Live");
        await host.SeedActiveProfileAsync();

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
                host.EnvironmentCatalog.AddEnvironment(CommandTestHost.DefaultEnvironmentUrl, null);
                break;
            // "missing": environment is simply not in the catalog.
        }
        await host.SeedActiveProfileAsync();

        var command = new FakeDestructiveCommand();
        Assert.Equal(2, await command.RunAsync());
        Assert.False(command.Executed);
        Assert.Null((await StoredConnectionAsync(host)).EnvironmentType);
    }

    [Fact]
    public async Task MissingType_CatalogHangs_TimesOutAndStaysBlocked()
    {
        using var host = new CommandTestHost();
        host.EnvironmentCatalog.HangUntilCancelled = true;
        await host.SeedActiveProfileAsync();

        var command = new FakeDestructiveCommand { LookupTimeout = TimeSpan.FromMilliseconds(100) };
        var run = command.RunAsync();

        Assert.Same(run, await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10))));
        Assert.Equal(2, await run);
        Assert.False(command.Executed);
    }

    [Fact]
    public async Task ConnectionUnreadable_BlocksDestructiveCommand()
    {
        CommandTestHost.FailingConnectionStore? store = null;
        using var host = new CommandTestHost(decorateConnectionStore: inner => store = new CommandTestHost.FailingConnectionStore(inner));
        await host.SeedActiveProfileAsync(EnvironmentType.Production);
        store!.FailReads = true;

        // Profile resolution fails with something other than a configuration error.
        var command = new FakeDestructiveCommand();
        Assert.NotEqual(0, await command.RunAsync());
        Assert.False(command.Executed);
    }

    [Fact]
    public async Task MissingType_CannotBeResolved_AllowProduction_Proceeds()
    {
        using var host = new CommandTestHost();
        host.EnvironmentCatalog.Failure = new HttpRequestException("proxy 502");
        await host.SeedActiveProfileAsync();

        var command = new FakeDestructiveCommand { AllowProduction = true };
        Assert.Equal(0, await command.RunAsync());
        Assert.True(command.Executed);
        Assert.Equal(0, host.EnvironmentCatalog.Lookups);
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
