using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using Xunit;
using ConnectionModel = TALXIS.CLI.Core.Model.Connection;

namespace TALXIS.CLI.Tests.Config.Commands;

[Collection("TxcServicesSerial")]
public sealed class EnvironmentTypeResolverTests
{
    private static readonly Guid EnvId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static void AddDeveloperEnvironment(CommandTestHost host)
        => host.EnvironmentCatalog.Add(new PowerPlatformEnvironmentSummary(
            EnvironmentId: EnvId,
            DisplayName: "Contoso DevBox",
            EnvironmentUrl: new Uri(ProductionGuardTestSupport.EnvUrl + "/"),
            UniqueName: "contoso-devbox",
            DomainName: "devbox",
            OrganizationId: Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            EnvironmentType: EnvironmentType.Developer));

    private static ConnectionModel UntypedConnection() => new()
    {
        Id = "conn",
        Provider = ProviderKind.Dataverse,
        EnvironmentUrl = ProductionGuardTestSupport.EnvUrl,
    };

    [Fact]
    public async Task Refresh_UpdatesConnectionAndPersists()
    {
        using var host = new CommandTestHost();
        AddDeveloperEnvironment(host);
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
        FailingConnectionStore? store = null;
        using var host = new CommandTestHost(decorateConnectionStore: inner => store = new FailingConnectionStore(inner));
        AddDeveloperEnvironment(host);
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
