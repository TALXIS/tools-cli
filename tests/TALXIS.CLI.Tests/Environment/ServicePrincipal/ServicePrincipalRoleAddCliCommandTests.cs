using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Environment.ServicePrincipal;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.ServicePrincipal;

/// <summary>
/// Regression coverage for <see cref="ServicePrincipalRoleAddCliCommand"/>'s idempotent
/// no-op behavior: re-running <c>role add</c> for a role that is already
/// assigned must report <c>"unchanged"</c> and must not call
/// <see cref="IDataverseServicePrincipalService.AddRoleAsync"/> again, matching the
/// equivalent behavior on <c>environment user role add</c>.
/// </summary>
[Collection("TxcServicesSerial")]
public sealed class ServicePrincipalRoleAddCliCommandTests
{
    private static readonly Guid RoleId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task RunAsync_RoleAlreadyAssigned_ReturnsUnchangedWithoutMutating()
    {
        var role = new DataverseRoleRecord(RoleId, "Owner", null, null);
        using var host = new FakeServicePrincipalServiceHost(existingRoles: new[] { role });

        var output = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(output))
        {
            exit = await new ServicePrincipalRoleAddCliCommand
            {
                Format = "json",
                ServicePrincipal = "11111111-1111-1111-1111-111111111111",
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
        using var host = new FakeServicePrincipalServiceHost(existingRoles: Array.Empty<DataverseRoleRecord>());

        var output = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(output))
        {
            exit = await new ServicePrincipalRoleAddCliCommand
            {
                Format = "json",
                ServicePrincipal = "11111111-1111-1111-1111-111111111111",
                Role = "Owner"
            }.RunAsync();
        }

        Assert.Equal(0, exit);
        Assert.True(host.Service.AddRoleAsyncCalled);
        Assert.Contains("\"status\": \"role-added\"", output.ToString());
    }

    private sealed class FakeServicePrincipalServiceHost : IDisposable
    {
        private readonly ServiceProvider _provider;

        public FakeServicePrincipalService Service { get; }

        public FakeServicePrincipalServiceHost(IReadOnlyList<DataverseRoleRecord> existingRoles)
        {
            Service = new FakeServicePrincipalService(existingRoles);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IDataverseServicePrincipalService>(Service);

            _provider = services.BuildServiceProvider();
            TxcServices.Initialize(_provider);
        }

        public void Dispose()
        {
            TxcServices.Reset();
            _provider.Dispose();
        }
    }

    private sealed class FakeServicePrincipalService(IReadOnlyList<DataverseRoleRecord> existingRoles) : IDataverseServicePrincipalService
    {
        public bool AddRoleAsyncCalled { get; private set; }

        public Task<IReadOnlyList<DataverseServicePrincipalRecord>> ListAsync(string? profileName, DataverseSecurityPrincipalStateFilter filter, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<DataverseServicePrincipalRecord?> GetAsync(string? profileName, string clientIdOrGuid, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<DataverseServicePrincipalRecord> CreateAsync(string? profileName, DataverseServicePrincipalCreateOptions options, CancellationToken ct)
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
