using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Environment.Team;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Team;

/// <summary>
/// Regression coverage for <see cref="TeamRoleAddCliCommand"/>'s idempotent
/// no-op behavior: re-running <c>role add</c> for a role that is already
/// assigned must report <c>"unchanged"</c> and must not call
/// <see cref="IDataverseTeamService.AddRoleAsync"/> again, matching the
/// equivalent behavior on <c>environment user role add</c>.
/// </summary>
[Collection("TxcServicesSerial")]
public sealed class TeamRoleAddCliCommandTests
{
    private static readonly Guid RoleId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task RunAsync_RoleAlreadyAssigned_ReturnsUnchangedWithoutMutating()
    {
        var role = new DataverseRoleRecord(RoleId, "Owner", null, null);
        using var host = new FakeTeamServiceHost(existingRoles: new[] { role });

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
        using var host = new FakeTeamServiceHost(existingRoles: Array.Empty<DataverseRoleRecord>());

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

    private sealed class FakeTeamServiceHost : IDisposable
    {
        private readonly ServiceProvider _provider;

        public FakeTeamService Service { get; }

        public FakeTeamServiceHost(IReadOnlyList<DataverseRoleRecord> existingRoles)
        {
            Service = new FakeTeamService(existingRoles);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IDataverseTeamService>(Service);

            _provider = services.BuildServiceProvider();
            TxcServices.Initialize(_provider);
        }

        public void Dispose()
        {
            TxcServices.Reset();
            _provider.Dispose();
        }
    }

    private sealed class FakeTeamService(IReadOnlyList<DataverseRoleRecord> existingRoles) : IDataverseTeamService
    {
        public bool AddRoleAsyncCalled { get; private set; }

        public Task<IReadOnlyList<DataverseTeamRecord>> ListAsync(string? profileName, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<DataverseTeamRecord?> GetAsync(string? profileName, string nameOrGuid, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<DataverseTeamRecord> CreateAsync(string? profileName, DataverseTeamCreateOptions options, CancellationToken ct)
            => throw new NotImplementedException();

        public Task DeleteAsync(string? profileName, string nameOrGuid, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<DataverseUserRecord>> ListMembersAsync(string? profileName, string teamIdOrName, CancellationToken ct)
            => throw new NotImplementedException();

        public Task AddMemberAsync(string? profileName, string teamIdOrName, string userIdOrUpn, CancellationToken ct)
            => throw new NotImplementedException();

        public Task RemoveMemberAsync(string? profileName, string teamIdOrName, string userIdOrUpn, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<DataverseRoleRecord>> ListRolesAsync(string? profileName, string teamIdOrName, CancellationToken ct)
            => Task.FromResult(existingRoles);

        public Task AddRoleAsync(string? profileName, string teamIdOrName, string roleNameOrGuid, CancellationToken ct)
        {
            AddRoleAsyncCalled = true;
            return Task.CompletedTask;
        }

        public Task RemoveRoleAsync(string? profileName, string teamIdOrName, string roleNameOrGuid, CancellationToken ct)
            => throw new NotImplementedException();
    }
}
