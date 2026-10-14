using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Features.Security.User;
using Xunit;

namespace TALXIS.CLI.Tests.Security.User;

[Collection("TxcServicesSerial")]
public sealed class UserRoleCliCommandTests
{
    private static readonly Guid EnvironmentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RoleId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task RunAsync_RoleAdd_AmbiguousMatchOnMutate_ReturnsValidationErrorWithCandidates()
    {
        var user = new DataverseUserRecord(UserId, "Alice Adams", "alice@contoso.com", null, null, false, null, null);
        var role = new DataverseRoleRecord(RoleId, "Owner", null, null);
        var candidates = new List<DataverseLookupCandidate>
        {
            new(RoleId, "Owner", "First owner role."),
            new(Guid.NewGuid(), "Owner", "Second owner role.")
        };

        using var host = CreateHost(
            user,
            role,
            existingRoles: Array.Empty<DataverseRoleRecord>(),
            mutateException: new DataverseAmbiguousMatchException("role", "Owner", candidates));

        var output = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(output))
        {
            exit = await new UserRoleAddCliCommand
            {
                Format = "json",
                User = "alice@contoso.com",
                Role = "Owner"
            }.RunAsync();
        }

        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public async Task RunAsync_RoleRemove_AmbiguousMatchOnMutate_ReturnsValidationErrorWithCandidates()
    {
        var user = new DataverseUserRecord(UserId, "Alice Adams", "alice@contoso.com", null, null, false, null, null);
        var role = new DataverseRoleRecord(RoleId, "Owner", null, null);
        var candidates = new List<DataverseLookupCandidate>
        {
            new(RoleId, "Owner", "First owner role."),
            new(Guid.NewGuid(), "Owner", "Second owner role.")
        };

        using var host = CreateHost(
            user,
            role,
            existingRoles: new[] { role },
            mutateException: new DataverseAmbiguousMatchException("role", "Owner", candidates));

        var output = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(output))
        {
            exit = await new UserRoleRemoveCliCommand
            {
                Format = "json",
                Yes = true,
                User = "alice@contoso.com",
                Role = "Owner"
            }.RunAsync();
        }

        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, output.ToString());
    }

    private static SecurityCommandTestHost CreateHost(
        DataverseUserRecord user,
        DataverseRoleRecord role,
        IReadOnlyList<DataverseRoleRecord> existingRoles,
        Exception mutateException)
    {
        var catalog = new SecurityCommandTestHost.FakePowerPlatformEnvironmentCatalog();
        catalog.Add(new(EnvironmentId, "Contoso Sandbox", new Uri("https://contoso.crm.dynamics.com/"), null, null, null, TALXIS.CLI.Core.Model.EnvironmentType.Sandbox));
        return new SecurityCommandTestHost(
            new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(),
            SecurityCommandTestHost.TestContext(includeEnvironment: true, environmentId: EnvironmentId),
            services =>
            {
                services.AddSingleton<IDataverseUserService>(new FakeUserService(user, existingRoles, mutateException));
                services.AddSingleton<IDataverseRoleService>(new FakeRoleService(role));
            },
            catalog);
    }

    private sealed class FakeUserService(
        DataverseUserRecord user,
        IReadOnlyList<DataverseRoleRecord> existingRoles,
        Exception mutateException) : IDataverseUserService
    {
        public Task<IReadOnlyList<DataverseUserRecord>> ListAsync(string? profileName, DataverseSecurityPrincipalStateFilter filter, CancellationToken ct, Guid? environmentId = null)
            => Task.FromResult<IReadOnlyList<DataverseUserRecord>>(new[] { user });

        public Task<DataverseUserRecord?> GetAsync(string? profileName, string userIdOrUpn, CancellationToken ct, Guid? environmentId = null)
            => Task.FromResult<DataverseUserRecord?>(user);

        public Task UpdateEnabledStateAsync(string? profileName, string userIdOrUpn, bool enabled, CancellationToken ct, Guid? environmentId = null)
            => Task.CompletedTask;

        public Task<IReadOnlyList<DataverseRoleRecord>> ListRolesAsync(string? profileName, string userIdOrUpn, CancellationToken ct, Guid? environmentId = null)
            => Task.FromResult(existingRoles);

        public Task AddRoleAsync(string? profileName, string userIdOrUpn, string roleNameOrGuid, CancellationToken ct, Guid? environmentId = null)
            => throw mutateException;

        public Task RemoveRoleAsync(string? profileName, string userIdOrUpn, string roleNameOrGuid, CancellationToken ct, Guid? environmentId = null)
            => throw mutateException;
    }

    private sealed class FakeRoleService(DataverseRoleRecord role) : IDataverseRoleService
    {
        public Task<IReadOnlyList<DataverseRoleRecord>> ListAsync(string? profileName, string? filter, CancellationToken ct, Guid? environmentId = null)
            => Task.FromResult<IReadOnlyList<DataverseRoleRecord>>(new[] { role });

        public Task<DataverseRoleRecord?> GetAsync(string? profileName, string nameOrGuid, CancellationToken ct, Guid? environmentId = null)
            => Task.FromResult<DataverseRoleRecord?>(role);
    }
}
