using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using TALXIS.CLI.Platform.Dataverse.Data;
using Xunit;

namespace TALXIS.CLI.Tests.Dataverse;

public class BulkBatchRunnerTests
{
    private static List<Entity> Records(int count) =>
        Enumerable.Range(0, count).Select(_ => new Entity("account")).ToList();

    [Fact]
    public async Task RunAsync_SplitsRecordsIntoBatches()
    {
        var batchSizes = new List<int>();

        var result = await BulkBatchRunner.RunAsync("account", Records(25), 10, (batch, _) =>
        {
            batchSizes.Add(batch.Entities.Count);
            IReadOnlyList<Guid> ids = batch.Entities.Select(_ => Guid.NewGuid()).ToList();
            return Task.FromResult((batch.Entities.Count, ids));
        }, CancellationToken.None);

        Assert.Equal(new[] { 10, 10, 5 }, batchSizes);
        Assert.Equal(25, result.SucceededCount);
        Assert.Equal(25, result.CreatedIds.Count);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task RunAsync_Timeout_ReportsBatchAsUnknownAndStops()
    {
        var calls = 0;

        var result = await BulkBatchRunner.RunAsync("account", Records(25), 10, (batch, _) =>
        {
            if (++calls == 2) throw new TimeoutException("The request channel timed out.");
            IReadOnlyList<Guid> ids = batch.Entities.Select(_ => Guid.NewGuid()).ToList();
            return Task.FromResult((batch.Entities.Count, ids));
        }, CancellationToken.None);

        Assert.Equal(2, calls);
        Assert.Equal(10, result.SucceededCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(10, result.UnknownCount);
        Assert.Equal(5, result.SkippedCount);
        Assert.Contains("records 11-20 of 25", result.Error);
        Assert.Contains("may still have committed", result.Error);
    }

    [Fact]
    public async Task RunAsync_ServerFault_ReportsBatchAsFailed()
    {
        var result = await BulkBatchRunner.RunAsync("account", Records(5), 10, (_, _) =>
            throw new FaultException<OrganizationServiceFault>(new OrganizationServiceFault(), "Plugin failed"),
            CancellationToken.None);

        Assert.Equal(0, result.SucceededCount);
        Assert.Equal(5, result.FailedCount);
        Assert.Equal(0, result.UnknownCount);
        Assert.Contains("Plugin failed", result.Error);
    }

    [Fact]
    public void IsTimeout_FindsWrappedTimeout()
    {
        var wrapped = new InvalidOperationException("outer", new TaskCanceledException("inner", new TimeoutException()));

        Assert.True(BulkBatchRunner.IsTimeout(wrapped));
        Assert.False(BulkBatchRunner.IsTimeout(new InvalidOperationException("no")));
    }
}
