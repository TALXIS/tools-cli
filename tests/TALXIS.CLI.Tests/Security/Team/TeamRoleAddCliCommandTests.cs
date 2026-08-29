using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Features.Security.Team;
using Xunit;

namespace TALXIS.CLI.Tests.Security.Team;

[Collection("TxcServicesSerial")]
public sealed class TeamRoleAddCliCommandTests
{
    private static readonly Guid EnvironmentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid RoleId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task RunAsync_RoleAlreadyAssigned_ReturnsUnchangedWithoutMutating()
    {
        var role = new DataverseRoleRecord(RoleId, "Owner", null, null);
        using var host = CreateHost(new[] { role });

        var output = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(output))
        {
            exit = await new TeamRoleAddCliCommand
            {
                Format = "json",
                Team = "Sales Team",
                Role = "Owner"
            }.RunAsync();
        }

        Assert.Equal(0, exit);
        Assert.False(host.Service.AddRoleAsyncCalled);
        Assert.Contains("\"status\": \"unchanged\"", output.ToString());
    }

    [Fact]
    public async Task RunAsync_RoleNotYetAssigned_AddsRoleAndReportsSucceeded()
    {
        using var host = CreateHost(Array.Empty<DataverseRoleRecord>());

        var output = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(output))
        {
            exit = await new TeamRoleAddCliCommand
            {
                Format = "json",
                Team = "Sales Team",
                Role = "Owner"
            }.RunAsync();
        }

        Assert.Equal(0, exit);
        Assert.True(host.Service.AddRoleAsyncCalled);
        Assert.Contains("\"status\": \"succeeded\"", output.ToString());
    }

    private static HostBundle CreateHost(IReadOnlyList<DataverseRoleRecord> existingRoles)
    {
        var service = new FakeTeamService(existingRoles);
        var catalog = new SecurityCommandTestHost.FakePowerPlatformEnvironmentCatalog();
        catalog.Add(new(EnvironmentId, "Contoso Sandbox", new Uri("https://contoso.crm.dynamics.com/"), null, null, null, TALXIS.CLI.Core.Model.EnvironmentType.Sandbox));
        var host = new SecurityCommandTestHost(
            new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(),
            SecurityCommandTestHost.TestContext(includeEnvironment: true, environmentId: EnvironmentId),
            services => services.AddSingleton<IDataverseTeamService>(service),
            catalog);
        return new HostBundle(host, service);
    }

    private sealed record HostBundle(SecurityCommandTestHost Host, FakeTeamService Service) : IDisposable
    {
        public void Dispose() => Host.Dispose();
    }

    private sealed class FakeTeamService(IReadOnlyList<DataverseRoleRecord> existingRoles) : IDataverseTeamService
    {
        public bool AddRoleAsyncCalled { get; private set; }

        public Task<IReadOnlyList<DataverseTeamRecord>> ListAsync(string? profileName, CancellationToken ct, Guid? environmentId = null)
            => throw new NotImplementedException();

        public Task<DataverseTeamRecord?> GetAsync(string? profileName, string nameOrGuid, CancellationToken ct, Guid? environmentId = null)
            => throw new NotImplementedException();

        public Task<DataverseTeamRecord> CreateAsync(string? profileName, DataverseTeamCreateOptions options, CancellationToken ct, Guid? environmentId = null)
            => throw new NotImplementedException();

        public Task DeleteAsync(string? profileName, string nameOrGuid, CancellationToken ct, Guid? environmentId = null)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<DataverseUserRecord>> ListMembersAsync(string? profileName, string teamIdOrName, CancellationToken ct, Guid? environmentId = null)
            => throw new NotImplementedException();

        public Task AddMemberAsync(string? profileName, string teamIdOrName, string userIdOrUpn, CancellationToken ct, Guid? environmentId = null)
            => throw new NotImplementedException();

        public Task RemoveMemberAsync(string? profileName, string teamIdOrName, string userIdOrUpn, CancellationToken ct, Guid? environmentId = null)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<DataverseRoleRecord>> ListRolesAsync(string? profileName, string teamIdOrName, CancellationToken ct, Guid? environmentId = null)
            => Task.FromResult(existingRoles);

        public Task AddRoleAsync(string? profileName, string teamIdOrName, string roleNameOrGuid, CancellationToken ct, Guid? environmentId = null)
        {
            AddRoleAsyncCalled = true;
            return Task.CompletedTask;
        }

        public Task RemoveRoleAsync(string? profileName, string teamIdOrName, string roleNameOrGuid, CancellationToken ct, Guid? environmentId = null)
            => throw new NotImplementedException();
    }
}
