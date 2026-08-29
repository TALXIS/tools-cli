using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Features.Security.ServicePrincipal;
using Xunit;

namespace TALXIS.CLI.Tests.Security.ServicePrincipal;

[Collection("TxcServicesSerial")]
public sealed class ServicePrincipalRoleAddCliCommandTests
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
        using var host = CreateHost(Array.Empty<DataverseRoleRecord>());

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

    private static HostBundle CreateHost(IReadOnlyList<DataverseRoleRecord> existingRoles)
    {
        var service = new FakeServicePrincipalService(existingRoles);
        var catalog = new SecurityCommandTestHost.FakePowerPlatformEnvironmentCatalog();
        catalog.Add(new(EnvironmentId, "Contoso Sandbox", new Uri("https://contoso.crm.dynamics.com/"), null, null, null, TALXIS.CLI.Core.Model.EnvironmentType.Sandbox));
        var host = new SecurityCommandTestHost(
            new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(),
            SecurityCommandTestHost.TestContext(includeEnvironment: true, environmentId: EnvironmentId),
            services => services.AddSingleton<IDataverseServicePrincipalService>(service),
            catalog);
        return new HostBundle(host, service);
    }

    private sealed record HostBundle(SecurityCommandTestHost Host, FakeServicePrincipalService Service) : IDisposable
    {
        public void Dispose() => Host.Dispose();
    }

    private sealed class FakeServicePrincipalService(IReadOnlyList<DataverseRoleRecord> existingRoles) : IDataverseServicePrincipalService
    {
        public bool AddRoleAsyncCalled { get; private set; }

        public Task<IReadOnlyList<DataverseServicePrincipalRecord>> ListAsync(string? profileName, DataverseSecurityPrincipalStateFilter filter, CancellationToken ct, Guid? environmentId = null)
            => throw new NotImplementedException();

        public Task<DataverseServicePrincipalRecord?> GetAsync(string? profileName, string clientIdOrGuid, CancellationToken ct, Guid? environmentId = null)
            => throw new NotImplementedException();

        public Task<DataverseServicePrincipalRecord> CreateAsync(string? profileName, DataverseServicePrincipalCreateOptions options, CancellationToken ct, Guid? environmentId = null)
            => throw new NotImplementedException();

        public Task UpdateEnabledStateAsync(string? profileName, string clientIdOrGuid, bool enabled, CancellationToken ct, Guid? environmentId = null)
            => throw new NotImplementedException();

        public Task DeleteAsync(string? profileName, string clientIdOrGuid, CancellationToken ct, Guid? environmentId = null)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<DataverseRoleRecord>> ListRolesAsync(string? profileName, string clientIdOrGuid, CancellationToken ct, Guid? environmentId = null)
            => Task.FromResult(existingRoles);

        public Task AddRoleAsync(string? profileName, string clientIdOrGuid, string roleNameOrGuid, CancellationToken ct, Guid? environmentId = null)
        {
            AddRoleAsyncCalled = true;
            return Task.CompletedTask;
        }

        public Task RemoveRoleAsync(string? profileName, string clientIdOrGuid, string roleNameOrGuid, CancellationToken ct, Guid? environmentId = null)
            => throw new NotImplementedException();
    }
}
