using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Environment.Component.Dependency;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Component;

[Collection("TxcServicesSerial")]
public sealed class ComponentDependencyDeleteCheckCliCommandTests
{
    private const string ComponentId = "11111111-1111-1111-1111-111111111111";

    [Fact]
    public async Task RunAsync_Blocked_ExitsZeroAndReportsBlocked()
    {
        var dependency = new DependencyRow(
            Guid.NewGuid(), Guid.NewGuid(), 60, Guid.Empty, Guid.Parse(ComponentId), 2, Guid.Empty, 2);
        using var host = new FakeDependencyServiceHost(new FakeDependencyService([dependency]));

        var (exit, output) = await RunAsync();

        Assert.Equal(0, exit);
        var root = JsonDocument.Parse(output).RootElement;
        Assert.Equal("blocked", root.GetProperty("status").GetString());
        Assert.Equal(1, root.GetProperty("blockingDependencies").GetInt32());
    }

    [Fact]
    public async Task RunAsync_Safe_ExitsZeroAndReportsSafe()
    {
        using var host = new FakeDependencyServiceHost(new FakeDependencyService([]));

        var (exit, output) = await RunAsync();

        Assert.Equal(0, exit);
        Assert.Equal("safe", JsonDocument.Parse(output).RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task RunAsync_ServiceFails_ExitsOne()
    {
        using var host = new FakeDependencyServiceHost(new FakeDependencyService(failure: new InvalidOperationException("Service unavailable.")));

        var (exit, _) = await RunAsync();

        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task RunAsync_InvalidInput_ExitsTwo()
    {
        using var host = new FakeDependencyServiceHost(new FakeDependencyService([]));

        var output = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(output))
        {
            exit = await new ComponentDependencyDeleteCheckCliCommand { Format = "json", Id = "not-a-guid", Type = "Attribute" }.RunAsync();
        }

        Assert.Equal(2, exit);
    }

    private static async Task<(int Exit, string Output)> RunAsync()
    {
        var output = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(output))
        {
            exit = await new ComponentDependencyDeleteCheckCliCommand { Format = "json", Id = ComponentId, Type = "Attribute" }.RunAsync();
        }

        return (exit, output.ToString());
    }

    private sealed class FakeDependencyServiceHost : IDisposable
    {
        private readonly ServiceProvider _provider;

        public FakeDependencyServiceHost(ISolutionDependencyService service)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(service);

            _provider = services.BuildServiceProvider();
            TxcServices.Initialize(_provider);
        }

        public void Dispose()
        {
            TxcServices.Reset();
            _provider.Dispose();
        }
    }

    private sealed class FakeDependencyService(IReadOnlyList<DependencyRow>? blocking = null, Exception? failure = null) : ISolutionDependencyService
    {
        public Task<IReadOnlyList<DependencyRow>> CheckDeleteAsync(string? profileName, Guid componentId, int componentType, CancellationToken ct)
        {
            if (failure is not null) throw failure;
            return Task.FromResult(blocking ?? []);
        }

        public Task<IReadOnlyList<DependencyRow>> CheckUninstallAsync(string? profileName, string solutionUniqueName, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<DependencyRow>> GetDependentsAsync(string? profileName, Guid componentId, int componentType, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<DependencyRow>> GetRequiredAsync(string? profileName, Guid componentId, int componentType, CancellationToken ct)
            => throw new NotImplementedException();
    }
}
