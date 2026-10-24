using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Features.Environment;
using TALXIS.CLI.Tests.Config.Commands;
using Xunit;

namespace TALXIS.CLI.Tests.Environment;

[Collection("TxcServicesSerial")]
public sealed class EnvironmentDeleteCliCommandTests
{
    private sealed class FakeEnvironmentManagement : IEnvironmentManagementService
    {
        public List<EnvironmentInfo> Environments { get; } = new();
        public Exception? ListFailure { get; set; }
        public int ListCalls { get; private set; }
        public int DeleteCalls { get; private set; }

        public Task<IReadOnlyList<EnvironmentInfo>> ListAsync(string? profileName, string? credentialId, CloudInstance? cloud, CancellationToken ct)
        {
            ListCalls++;
            if (ListFailure is not null) throw ListFailure;
            return Task.FromResult<IReadOnlyList<EnvironmentInfo>>(Environments.ToList());
        }

        public Task<EnvironmentDeleteOutcome> DeleteAsync(string? profileName, Guid environmentId, bool wait, TimeSpan maxWait, CancellationToken ct)
        {
            DeleteCalls++;
            return Task.FromResult(new EnvironmentDeleteOutcome(environmentId, "Queued", Completed: false, OperationLocation: null));
        }

        public Task<EnvironmentCreateOutcome> CreateAsync(string? profileName, EnvironmentCreateOptions options, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<EnvironmentUpdateOutcome> UpdateAsync(string? profileName, EnvironmentUpdateOptions options, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private static readonly Guid TargetEnvId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static EnvironmentInfo TargetEnv(EnvironmentType? type, string displayName = "Contoso DevBox")
        => new(TargetEnvId, displayName, new Uri(CommandTestHost.DefaultEnvironmentUrl + "/"), "contoso-devbox", Guid.NewGuid(), type);

    private static async Task<(int Exit, FakeEnvironmentManagement Service)> RunEnvDeleteAsync(
        FakeEnvironmentManagement service, Func<EnvironmentDeleteCliCommand>? create = null, bool seed = true)
    {
        using var host = new CommandTestHost(environmentManagement: service);
        if (seed) await host.SeedActiveProfileAsync();
        var command = create?.Invoke() ?? new EnvironmentDeleteCliCommand { EnvironmentId = TargetEnvId, Yes = true };
        int exit;
        using (OutputWriter.RedirectTo(new StringWriter())) { exit = await command.RunAsync(); }
        return (exit, service);
    }

    [Fact]
    public async Task EnvDelete_DeveloperTarget_Proceeds()
    {
        var service = new FakeEnvironmentManagement { Environments = { TargetEnv(EnvironmentType.Developer) } };
        var (exit, _) = await RunEnvDeleteAsync(service);
        Assert.Equal(0, exit);
        Assert.Equal(1, service.DeleteCalls);
    }

    [Fact]
    public async Task EnvDelete_ProductionTarget_IsBlocked()
    {
        var service = new FakeEnvironmentManagement { Environments = { TargetEnv(EnvironmentType.Production) } };
        var (exit, _) = await RunEnvDeleteAsync(service);
        Assert.Equal(2, exit);
        Assert.Equal(0, service.DeleteCalls);
    }

    [Fact]
    public async Task EnvDelete_TargetTypeUnknown_IsBlocked()
    {
        var service = new FakeEnvironmentManagement { Environments = { TargetEnv(type: null) } };
        var (exit, _) = await RunEnvDeleteAsync(service);
        Assert.Equal(2, exit);
        Assert.Equal(0, service.DeleteCalls);
    }

    [Fact]
    public async Task EnvDelete_TargetNotInCatalog_IsBlocked()
    {
        var service = new FakeEnvironmentManagement();
        var (exit, _) = await RunEnvDeleteAsync(service);
        Assert.Equal(2, exit);
        Assert.Equal(0, service.DeleteCalls);
    }

    [Fact]
    public async Task EnvDelete_CatalogLookupFails_IsBlocked()
    {
        var service = new FakeEnvironmentManagement { ListFailure = new HttpRequestException("admin endpoint unavailable") };
        var (exit, _) = await RunEnvDeleteAsync(service);
        Assert.Equal(2, exit);
        Assert.Equal(0, service.DeleteCalls);
    }

    [Fact]
    public async Task EnvDelete_UnresolvableProfile_IsBlocked()
    {
        var service = new FakeEnvironmentManagement { Environments = { TargetEnv(EnvironmentType.Developer) } };
        var (exit, _) = await RunEnvDeleteAsync(service, seed: false);
        Assert.Equal(2, exit);
        Assert.Equal(0, service.DeleteCalls);
    }

    [Fact]
    public async Task EnvDelete_AllowProduction_SkipsVerification()
    {
        var service = new FakeEnvironmentManagement { ListFailure = new HttpRequestException("admin endpoint unavailable") };
        var (exit, _) = await RunEnvDeleteAsync(service,
            () => new EnvironmentDeleteCliCommand { EnvironmentId = TargetEnvId, Yes = true, AllowProduction = true });
        Assert.Equal(0, exit);
        Assert.Equal(0, service.ListCalls);
        Assert.Equal(1, service.DeleteCalls);
    }
}
