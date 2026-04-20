#pragma warning disable MCPEXP001

using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using TALXIS.CLI.MCP;
using Xunit;

namespace TALXIS.CLI.Tests.MCP;

public class CancellableTaskStoreTests
{
    private CancellableTaskStore CreateStore()
    {
        var inner = new InMemoryMcpTaskStore(
            defaultTtl: TimeSpan.FromMinutes(5),
            pollInterval: TimeSpan.FromSeconds(1));
        return new CancellableTaskStore(inner);
    }

    private async Task<McpTask> CreateTestTaskAsync(CancellableTaskStore store)
    {
        var metadata = new McpTaskMetadata();
        var requestId = new RequestId("test-req-1");
        var request = new JsonRpcRequest { Method = "tools/call", Id = requestId };
        return await store.CreateTaskAsync(metadata, requestId, request, "session-1", CancellationToken.None);
    }

    [Fact]
    public async Task CancelTaskAsync_CancelsRegisteredCts()
    {
        var store = CreateStore();
        var task = await CreateTestTaskAsync(store);

        var cts = new CancellationTokenSource();
        store.RegisterCancellationToken(task.TaskId, cts);

        Assert.False(cts.IsCancellationRequested);

        await store.CancelTaskAsync(task.TaskId, "session-1", CancellationToken.None);

        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public async Task CancelTaskAsync_WithoutRegisteredCts_DoesNotThrow()
    {
        var store = CreateStore();
        var task = await CreateTestTaskAsync(store);

        // No CTS registered — should not throw
        var result = await store.CancelTaskAsync(task.TaskId, "session-1", CancellationToken.None);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task CancelTaskAsync_WithDisposedCts_DoesNotThrow()
    {
        var store = CreateStore();
        var task = await CreateTestTaskAsync(store);

        var cts = new CancellationTokenSource();
        store.RegisterCancellationToken(task.TaskId, cts);
        cts.Dispose();

        // Should handle ObjectDisposedException gracefully
        var result = await store.CancelTaskAsync(task.TaskId, "session-1", CancellationToken.None);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task UnregisterCancellationToken_PreventsCancel()
    {
        var store = CreateStore();
        var task = await CreateTestTaskAsync(store);

        var cts = new CancellationTokenSource();
        store.RegisterCancellationToken(task.TaskId, cts);
        store.UnregisterCancellationToken(task.TaskId);

        await store.CancelTaskAsync(task.TaskId, "session-1", CancellationToken.None);

        Assert.False(cts.IsCancellationRequested);
    }

    [Fact]
    public async Task DelegatesCreateAndGetToInnerStore()
    {
        var store = CreateStore();
        var task = await CreateTestTaskAsync(store);

        var retrieved = await store.GetTaskAsync(task.TaskId, "session-1", CancellationToken.None);
        Assert.Equal(task.TaskId, retrieved.TaskId);
    }
}
