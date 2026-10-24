using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Tests.Config.Commands;
using Xunit;
using ConnectionModel = TALXIS.CLI.Core.Model.Connection;

namespace TALXIS.CLI.Tests.Config.Providers.Dataverse;

[Collection("TxcServicesSerial")]
public sealed class DataverseEnvironmentTypeResolverTests
{
    private static ConnectionModel UntypedConnection() => new()
    {
        Id = "conn",
        Provider = ProviderKind.Dataverse,
        EnvironmentUrl = CommandTestHost.DefaultEnvironmentUrl,
    };

    [Fact]
    public async Task Refresh_UpdatesConnectionAndPersists()
    {
        using var host = new CommandTestHost();
        host.EnvironmentCatalog.AddEnvironment(CommandTestHost.DefaultEnvironmentUrl, EnvironmentType.Developer);
        var resolver = (IEnvironmentTypeResolver)host.Provider.GetService(typeof(IEnvironmentTypeResolver))!;
        var connection = UntypedConnection();

        var type = await resolver.RefreshAsync(connection, new Credential { Id = "cred" }, default);

        Assert.Equal(EnvironmentType.Developer, type);
        Assert.Equal(EnvironmentType.Developer, connection.EnvironmentType);
        var stored = await ((IConnectionStore)host.Provider.GetService(typeof(IConnectionStore))!).GetAsync("conn", default);
        Assert.Equal(EnvironmentType.Developer, stored!.EnvironmentType);
    }

    [Fact]
    public async Task Refresh_WhenSaveFails_LeavesConnectionUnchanged()
    {
        CommandTestHost.FailingConnectionStore? store = null;
        using var host = new CommandTestHost(decorateConnectionStore: inner => store = new CommandTestHost.FailingConnectionStore(inner));
        host.EnvironmentCatalog.AddEnvironment(CommandTestHost.DefaultEnvironmentUrl, EnvironmentType.Developer);
        _ = host.Provider.GetService(typeof(IConnectionStore)); // the decorator is created on first resolve
        store!.FailUpserts = true;
        var resolver = (IEnvironmentTypeResolver)host.Provider.GetService(typeof(IEnvironmentTypeResolver))!;
        var connection = UntypedConnection();

        await Assert.ThrowsAsync<IOException>(() => resolver.RefreshAsync(connection, new Credential { Id = "cred" }, default));

        // Nothing was persisted, so the in-memory connection must not claim otherwise.
        Assert.Null(connection.EnvironmentType);
        Assert.Null(connection.DisplayName);
        Assert.Null(connection.OrganizationId);
        Assert.Null(connection.EnvironmentId);
    }
}
