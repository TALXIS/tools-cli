using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Environment.App;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.App;

/// <summary>
/// Regression coverage for <see cref="AppRoleAddCliCommand"/>'s idempotent
/// no-op behavior: re-running <c>role add</c> for a role that is already
/// assigned must report <c>"unchanged"</c> and must not call
/// <see cref="IDataverseAppUserService.AddRoleAsync"/> again, matching the
/// equivalent behavior on <c>environment user role add</c>.
/// </summary>
[Collection("TxcServicesSerial")]
public sealed class AppRoleAddCliCommandTests
{
    private static readonly Guid RoleId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task RunAsync_RoleAlreadyAssigned_ReturnsUnchangedWithoutMutating()
    {
        var role = new DataverseRoleRecord(RoleId, "Owner", null, null);
        using var host = new FakeAppUserServiceHost(existingRoles: new[] { role });

        var output = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(output))
        {
            exit = await new AppRoleAddCliCommand
            {
                Format = "json",
                App = "11111111-1111-1111-1111-111111111111",
                Role = "Owner"
            }.RunAsync();
        }

        Assert.Equal(0, exit);
        Assert.False(host.Service.AddRoleAsyncCalled);
        Assert.Contains("\"status\": \"unchanged\"", output.ToString());
    }

    [Fact]
    public async Task RunAsync_RoleNotYetAssigned_AddsRoleAndReportsRoleAdded()
    {
        using var host = new FakeAppUserServiceHost(existingRoles: Array.Empty<DataverseRoleRecord>());

        var output = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(output))
        {
            exit = await new AppRoleAddCliCommand
            {
                Format = "json",
                App = "11111111-1111-1111-1111-111111111111",
                Role = "Owner"
            }.RunAsync();
        }

        Assert.Equal(0, exit);
        Assert.True(host.Service.AddRoleAsyncCalled);
        Assert.Contains("\"status\": \"role-added\"", output.ToString());
    }

    private sealed class FakeAppUserServiceHost : IDisposable
    {
        private readonly ServiceProvider _provider;

        public FakeAppUserService Service { get; }

        public FakeAppUserServiceHost(IReadOnlyList<DataverseRoleRecord> existingRoles)
        {
            Service = new FakeAppUserService(existingRoles);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IDataverseAppUserService>(Service);

            _provider = services.BuildServiceProvider();
            TxcServices.Initialize(_provider);
        }

        public void Dispose()
        {
            TxcServices.Reset();
            _provider.Dispose();
        }
    }

    private sealed class FakeAppUserService(IReadOnlyList<DataverseRoleRecord> existingRoles) : IDataverseAppUserService
    {
        public bool AddRoleAsyncCalled { get; private set; }

        public Task<IReadOnlyList<DataverseAppUserRecord>> ListAsync(string? profileName, DataverseSecurityPrincipalStateFilter filter, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<DataverseAppUserRecord?> GetAsync(string? profileName, string clientIdOrGuid, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<DataverseAppUserRecord> CreateAsync(string? profileName, DataverseAppUserCreateOptions options, CancellationToken ct)
            => throw new NotImplementedException();

        public Task UpdateEnabledStateAsync(string? profileName, string clientIdOrGuid, bool enabled, CancellationToken ct)
            => throw new NotImplementedException();

        public Task DeleteAsync(string? profileName, string clientIdOrGuid, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<DataverseRoleRecord>> ListRolesAsync(string? profileName, string clientIdOrGuid, CancellationToken ct)
            => Task.FromResult(existingRoles);

        public Task AddRoleAsync(string? profileName, string clientIdOrGuid, string roleNameOrGuid, CancellationToken ct)
        {
            AddRoleAsyncCalled = true;
            return Task.CompletedTask;
        }

        public Task RemoveRoleAsync(string? profileName, string clientIdOrGuid, string roleNameOrGuid, CancellationToken ct)
            => throw new NotImplementedException();
    }
}
