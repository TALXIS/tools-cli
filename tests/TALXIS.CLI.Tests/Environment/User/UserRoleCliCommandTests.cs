using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Features.Environment.User;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.User;

/// <summary>
/// Regression coverage for the exit-code/candidate-listing behavior of
/// <see cref="UserRoleAddCliCommand"/> and <see cref="UserRoleRemoveCliCommand"/>
/// when the final Dataverse mutation throws
/// <see cref="DataverseAmbiguousMatchException"/> after resolution has
/// already succeeded (e.g. a race between resolve and mutate). Mirrors the
/// equivalent App/Team role command tests.
/// </summary>
[Collection("TxcServicesSerial")]
public sealed class UserRoleCliCommandTests
{
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

        using var host = new FakeUserServiceHost(
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

        using var host = new FakeUserServiceHost(
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

    private sealed class FakeUserServiceHost : IDisposable
    {
        private readonly ServiceProvider _provider;

        public FakeUserServiceHost(
            DataverseUserRecord user,
            DataverseRoleRecord role,
            IReadOnlyList<DataverseRoleRecord> existingRoles,
            Exception mutateException)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IDataverseUserService>(new FakeUserService(user, existingRoles, mutateException));
            services.AddSingleton<IDataverseRoleService>(new FakeRoleService(role));

            _provider = services.BuildServiceProvider();
            TxcServices.Initialize(_provider);
        }

        public void Dispose()
        {
            TxcServices.Reset();
            _provider.Dispose();
        }
    }

    private sealed class FakeUserService(
        DataverseUserRecord user,
        IReadOnlyList<DataverseRoleRecord> existingRoles,
        Exception mutateException) : IDataverseUserService
    {
        public Task<IReadOnlyList<DataverseUserRecord>> ListAsync(string? profileName, DataverseSecurityPrincipalStateFilter filter, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<DataverseUserRecord>>(new[] { user });

        public Task<DataverseUserRecord?> GetAsync(string? profileName, string userIdOrUpn, CancellationToken ct)
            => Task.FromResult<DataverseUserRecord?>(user);

        public Task UpdateEnabledStateAsync(string? profileName, string userIdOrUpn, bool enabled, CancellationToken ct)
            => Task.CompletedTask;

        public Task<IReadOnlyList<DataverseRoleRecord>> ListRolesAsync(string? profileName, string userIdOrUpn, CancellationToken ct)
            => Task.FromResult(existingRoles);

        public Task AddRoleAsync(string? profileName, string userIdOrUpn, string roleNameOrGuid, CancellationToken ct)
            => throw mutateException;

        public Task RemoveRoleAsync(string? profileName, string userIdOrUpn, string roleNameOrGuid, CancellationToken ct)
            => throw mutateException;
    }

    private sealed class FakeRoleService(DataverseRoleRecord role) : IDataverseRoleService
    {
        public Task<IReadOnlyList<DataverseRoleRecord>> ListAsync(string? profileName, string? filter, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<DataverseRoleRecord>>(new[] { role });

        public Task<DataverseRoleRecord?> GetAsync(string? profileName, string nameOrGuid, CancellationToken ct)
            => Task.FromResult<DataverseRoleRecord?>(role);
    }
}
