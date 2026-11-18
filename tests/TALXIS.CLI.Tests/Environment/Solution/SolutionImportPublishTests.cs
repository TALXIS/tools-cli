using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Features.Environment.Solution;
using TALXIS.CLI.Tests.Config.Commands;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Solution;

[Collection("TxcServicesSerial")]
public sealed class SolutionImportPublishTests : IDisposable
{
    private const string CompletedStatus = "Succeeded";
    private const string SkippedStatus = "Skipped (source version not higher than target; --skip-lower-version).";

    private readonly string _zipPath = Path.Combine(Path.GetTempPath(), $"txc_import_test_{Guid.NewGuid():N}.zip");

    public SolutionImportPublishTests() => File.WriteAllBytes(_zipPath, [0x50, 0x4B]);

    public void Dispose() => File.Delete(_zipPath);

    private sealed class FakeImportService(string status) : ISolutionImportService
    {
        public SolutionImportOptions? LastOptions { get; private set; }

        public Task<SolutionImportResult> ImportAsync(string? profileName, string solutionZipPath, SolutionImportOptions options, CancellationToken ct)
        {
            LastOptions = options;
            return Task.FromResult(new SolutionImportResult(
                SolutionImportPath.Update,
                new SolutionInfo("contoso_core", new Version(1, 0, 0, 1), Managed: false),
                new SolutionInfo("contoso_core", new Version(1, 0, 0, 0), Managed: false),
                Guid.NewGuid(),
                AsyncOperationId: null,
                DateTime.UtcNow,
                DateTime.UtcNow,
                SmartDiffExpected: false,
                status));
        }
    }

    private sealed class FakePublishService : ISolutionPublishService
    {
        public int Calls { get; private set; }
        public IReadOnlyList<string>? LastEntities { get; private set; }
        public Exception? Failure { get; set; }

        public Task PublishAsync(string? profileName, IReadOnlyList<string>? entityLogicalNames, CancellationToken ct)
        {
            Calls++;
            LastEntities = entityLogicalNames;
            if (Failure is not null) throw Failure;
            return Task.CompletedTask;
        }
    }

    private async Task<(int Exit, string Output)> RunAsync(
        FakeImportService import, FakePublishService publish, bool publishFlag, bool wait = false)
    {
        using var host = new CommandTestHost(configureServices: services =>
        {
            services.AddSingleton<ISolutionImportService>(import);
            services.AddSingleton<ISolutionPublishService>(publish);
        });
        await host.SeedActiveProfileAsync();

        var command = new SolutionImportCliCommand { SolutionZip = _zipPath, Publish = publishFlag, Wait = wait, Format = "json" };
        var output = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(output)) { exit = await command.RunAsync(); }
        return (exit, output.ToString());
    }

    [Fact]
    public async Task Publish_ForcesWaitedImport_AndPublishesAll()
    {
        var import = new FakeImportService(CompletedStatus);
        var publish = new FakePublishService();

        var (exit, output) = await RunAsync(import, publish, publishFlag: true);

        Assert.Equal(0, exit);
        Assert.False(import.LastOptions!.Async);
        Assert.Equal(1, publish.Calls);
        Assert.Null(publish.LastEntities);
        using var json = JsonDocument.Parse(output);
        Assert.True(json.RootElement.GetProperty("published").GetBoolean());
    }

    [Fact]
    public async Task NoPublish_KeepsQueuedImport_AndDoesNotPublish()
    {
        var import = new FakeImportService(CompletedStatus);
        var publish = new FakePublishService();

        var (exit, output) = await RunAsync(import, publish, publishFlag: false);

        Assert.Equal(0, exit);
        Assert.True(import.LastOptions!.Async);
        Assert.Equal(0, publish.Calls);
        using var json = JsonDocument.Parse(output);
        Assert.False(json.RootElement.GetProperty("published").GetBoolean());
    }

    [Fact]
    public async Task Wait_WithoutPublish_IsWaitedButNotPublished()
    {
        var import = new FakeImportService(CompletedStatus);
        var publish = new FakePublishService();

        var (exit, _) = await RunAsync(import, publish, publishFlag: false, wait: true);

        Assert.Equal(0, exit);
        Assert.False(import.LastOptions!.Async);
        Assert.Equal(0, publish.Calls);
    }

    [Fact]
    public async Task Publish_SkippedImport_DoesNotPublish()
    {
        var import = new FakeImportService(SkippedStatus);
        var publish = new FakePublishService();

        var (exit, output) = await RunAsync(import, publish, publishFlag: true);

        Assert.Equal(0, exit);
        Assert.Equal(0, publish.Calls);
        using var json = JsonDocument.Parse(output);
        Assert.False(json.RootElement.GetProperty("published").GetBoolean());
    }

    [Fact]
    public async Task Publish_Failure_FailsTheCommand()
    {
        var import = new FakeImportService(CompletedStatus);
        var publish = new FakePublishService { Failure = new InvalidOperationException("PublishAll failed") };

        var (exit, _) = await RunAsync(import, publish, publishFlag: true);

        Assert.NotEqual(0, exit);
        Assert.Equal(1, publish.Calls);
    }
}
