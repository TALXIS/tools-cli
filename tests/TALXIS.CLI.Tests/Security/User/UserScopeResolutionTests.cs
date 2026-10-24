using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Features.Security.Team;
using TALXIS.CLI.Features.Security.User;
using Xunit;

namespace TALXIS.CLI.Tests.Security.User;

[Collection("TxcServicesSerial")]
public sealed class UserScopeResolutionTests
{
    private static readonly Guid ActiveEnvironmentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ExplicitEnvironmentId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task UserList_ExplicitEnvironmentOverridesActiveConnection()
    {
        var service = new RecordingUserService();
        var catalog = new SecurityCommandTestHost.FakePowerPlatformEnvironmentCatalog();
        catalog.Add(new(ActiveEnvironmentId, "Active", new Uri("https://active.crm.dynamics.com/"), null, null, null, EnvironmentType.Sandbox));
        catalog.Add(new(ExplicitEnvironmentId, "Explicit", new Uri("https://explicit.crm.dynamics.com/"), null, null, null, EnvironmentType.Sandbox));

        using var host = new SecurityCommandTestHost(
            new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(),
            SecurityCommandTestHost.TestContext(includeEnvironment: true, environmentId: ActiveEnvironmentId),
            services => services.AddSingleton<IDataverseUserService>(service),
            catalog);

        var exit = await new UserListCliCommand { Environment = ExplicitEnvironmentId }.RunAsync();

        Assert.Equal(0, exit);
        Assert.Equal(ExplicitEnvironmentId, service.LastEnvironmentId);
    }

    [Fact]
    public async Task UserList_ActiveConnectionFallsBackToEnvironmentScope()
    {
        var service = new RecordingUserService();
        var catalog = new SecurityCommandTestHost.FakePowerPlatformEnvironmentCatalog();
        catalog.Add(new(ActiveEnvironmentId, "Active", new Uri("https://active.crm.dynamics.com/"), null, null, null, EnvironmentType.Sandbox));

        using var host = new SecurityCommandTestHost(
            new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(),
            SecurityCommandTestHost.TestContext(includeEnvironment: true, environmentId: ActiveEnvironmentId),
            services => services.AddSingleton<IDataverseUserService>(service),
            catalog);

        var exit = await new UserListCliCommand().RunAsync();

        Assert.Equal(0, exit);
        Assert.Equal(ActiveEnvironmentId, service.LastEnvironmentId);
    }

    [Fact]
    public async Task UserList_WithoutResolvedEnvironmentFallsBackToTenantUsers()
    {
        var service = new RecordingUserService();
        using var host = new SecurityCommandTestHost(
            new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
                _ => SecurityCommandTestHost.JsonResponse("""
                {
                  "value": [
                    {
                      "id": "11111111-1111-1111-1111-111111111111",
                      "userPrincipalName": "alice@contoso.com",
                      "displayName": "Alice Adams"
                    }
                  ]
                }
                """)
            ]),
            SecurityCommandTestHost.TestContext(includeEnvironment: false),
            services => services.AddSingleton<IDataverseUserService>(service));

        var output = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(output))
        {
            exit = await new UserListCliCommand { Format = "json" }.RunAsync();
        }

        Assert.Equal(0, exit);
        Assert.Null(service.LastEnvironmentId);
        Assert.Contains("alice@contoso.com", output.ToString());
    }

    [Fact]
    public async Task TeamList_WithoutResolvableEnvironment_ReturnsValidationError()
    {
        using var host = new SecurityCommandTestHost(new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(), SecurityCommandTestHost.TestContext(includeEnvironment: false));
        var exit = await new TeamListCliCommand().RunAsync();
        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task ServicePrincipalRoleList_WithEnvironmentScope_ReturnsTenantAndEnvironmentSections()
    {
        var catalog = new SecurityCommandTestHost.FakePowerPlatformEnvironmentCatalog();
        catalog.Add(new(ActiveEnvironmentId, "Active", new Uri("https://active.crm.dynamics.com/"), null, null, null, EnvironmentType.Sandbox));

        using var host = new SecurityCommandTestHost(
            new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
                _ => SecurityCommandTestHost.JsonResponse("""
                {
                  "value": [
                    {
                      "id": "11111111-1111-1111-1111-111111111111",
                      "appId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                      "displayName": "Contoso CLI"
                    }
                  ]
                }
                """),
                _ => SecurityCommandTestHost.JsonResponse("""
                {
                  "value": [
                    {
                      "roleDefinitionId": "33333333-3333-3333-3333-333333333333",
                      "roleDefinitionName": "Tenant Reader",
                      "description": "Can read tenant settings.",
                      "assignableScopes": ["/tenants/tenant-id"]
                    }
                  ]
                }
                """),
                _ => SecurityCommandTestHost.JsonResponse("""
                {
                  "value": [
                    {
                      "roleAssignmentId": "assign-1",
                      "roleDefinitionId": "33333333-3333-3333-3333-333333333333",
                      "scope": "/tenants/tenant-id",
                      "principalType": "ApplicationUser",
                      "principalObjectId": "11111111-1111-1111-1111-111111111111"
                    }
                  ]
                }
                """),
                _ => SecurityCommandTestHost.JsonResponse("""
                [
                  {
                    "applicationId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
                  }
                ]
                """)
            ]),
            SecurityCommandTestHost.TestContext(includeEnvironment: true, environmentId: ActiveEnvironmentId),
            services => services.AddSingleton<IDataverseServicePrincipalService>(new FakeScopedServicePrincipalService()),
            catalog);

        var output = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(output))
        {
            exit = await new TALXIS.CLI.Features.Security.ServicePrincipal.ServicePrincipalRoleListCliCommand
            {
                Format = "json",
                ServicePrincipal = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
            }.RunAsync();
        }

        Assert.Equal(0, exit);
        var document = JsonDocument.Parse(output.ToString());
        Assert.True(document.RootElement.TryGetProperty("tenantAdminRoles", out var tenantRoles));
        Assert.True(document.RootElement.TryGetProperty("environmentSecurityRoles", out var environmentRoles));
        Assert.True(tenantRoles.GetArrayLength() >= 1);
        Assert.Equal(1, environmentRoles.GetArrayLength());
        Assert.Equal("System Administrator", environmentRoles[0].GetProperty("name").GetString());
    }

    private sealed class RecordingUserService : IDataverseUserService
    {
        public Guid? LastEnvironmentId { get; private set; }

        public Task<IReadOnlyList<DataverseUserRecord>> ListAsync(string? profileName, DataverseSecurityPrincipalStateFilter filter, CancellationToken ct, Guid? environmentId = null)
        {
            LastEnvironmentId = environmentId;
            return Task.FromResult<IReadOnlyList<DataverseUserRecord>>(Array.Empty<DataverseUserRecord>());
        }

        public Task<DataverseUserRecord?> GetAsync(string? profileName, string userIdOrUpn, CancellationToken ct, Guid? environmentId = null)
            => throw new NotImplementedException();

        public Task UpdateEnabledStateAsync(string? profileName, string userIdOrUpn, bool enabled, CancellationToken ct, Guid? environmentId = null)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<DataverseRoleRecord>> ListRolesAsync(string? profileName, string userIdOrUpn, CancellationToken ct, Guid? environmentId = null)
            => throw new NotImplementedException();

        public Task AddRoleAsync(string? profileName, string userIdOrUpn, string roleNameOrGuid, CancellationToken ct, Guid? environmentId = null)
            => throw new NotImplementedException();

        public Task RemoveRoleAsync(string? profileName, string userIdOrUpn, string roleNameOrGuid, CancellationToken ct, Guid? environmentId = null)
            => throw new NotImplementedException();
    }

    private sealed class FakeScopedServicePrincipalService : IDataverseServicePrincipalService
    {
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
            => Task.FromResult<IReadOnlyList<DataverseRoleRecord>>([
                new DataverseRoleRecord(Guid.Parse("44444444-4444-4444-4444-444444444444"), "System Administrator", null, "Root Business Unit")
            ]);

        public Task AddRoleAsync(string? profileName, string clientIdOrGuid, string roleNameOrGuid, CancellationToken ct, Guid? environmentId = null)
            => throw new NotImplementedException();

        public Task RemoveRoleAsync(string? profileName, string clientIdOrGuid, string roleNameOrGuid, CancellationToken ct, Guid? environmentId = null)
            => throw new NotImplementedException();
    }
}
