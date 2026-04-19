#pragma warning disable MCPEXP001

using System.Collections.Concurrent;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace TALXIS.CLI.MCP;

/// <summary>
/// Wraps an <see cref="IMcpTaskStore"/> to propagate cancellation to running
/// subprocesses.  When the SDK calls <see cref="CancelTaskAsync"/> (in
/// response to a <c>tasks/cancel</c> request from the client), any
/// <see cref="CancellationTokenSource"/> registered for that task is
/// cancelled, which terminates the subprocess.
/// </summary>
public sealed class CancellableTaskStore : IMcpTaskStore
{
    private readonly IMcpTaskStore _inner;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _tokenSources = new();

    public CancellableTaskStore(IMcpTaskStore inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <summary>
    /// Associates a <see cref="CancellationTokenSource"/> with a task so that
    /// <see cref="CancelTaskAsync"/> can cancel it.
    /// </summary>
    public void RegisterCancellationToken(string taskId, CancellationTokenSource cts)
        => _tokenSources[taskId] = cts;

    /// <summary>
    /// Removes the association after the task completes (or is cancelled).
    /// </summary>
    public void UnregisterCancellationToken(string taskId)
        => _tokenSources.TryRemove(taskId, out _);

    public async Task<McpTask> CancelTaskAsync(
        string taskId, string sessionId, CancellationToken cancellationToken)
    {
        // Signal the subprocess CTS before updating the store so that the
        // background Task.Run observes the cancellation promptly.
        if (_tokenSources.TryGetValue(taskId, out var cts))
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
        }

        return await _inner.CancelTaskAsync(taskId, sessionId, cancellationToken);
    }

    public Task<McpTask> CreateTaskAsync(McpTaskMetadata taskParams, RequestId requestId, JsonRpcRequest request, string sessionId, CancellationToken cancellationToken)
        => _inner.CreateTaskAsync(taskParams, requestId, request, sessionId, cancellationToken);

    public Task<McpTask> GetTaskAsync(string taskId, string sessionId, CancellationToken cancellationToken)
        => _inner.GetTaskAsync(taskId, sessionId, cancellationToken);

    public Task<McpTask> StoreTaskResultAsync(string taskId, McpTaskStatus status, JsonElement result, string sessionId, CancellationToken cancellationToken)
        => _inner.StoreTaskResultAsync(taskId, status, result, sessionId, cancellationToken);

    public Task<JsonElement> GetTaskResultAsync(string taskId, string sessionId, CancellationToken cancellationToken)
        => _inner.GetTaskResultAsync(taskId, sessionId, cancellationToken);

    public Task<McpTask> UpdateTaskStatusAsync(string taskId, McpTaskStatus status, string? statusMessage, string sessionId, CancellationToken cancellationToken)
        => _inner.UpdateTaskStatusAsync(taskId, status, statusMessage, sessionId, cancellationToken);

    public Task<ListTasksResult> ListTasksAsync(string? cursor, string sessionId, CancellationToken cancellationToken)
        => _inner.ListTasksAsync(cursor, sessionId, cancellationToken);
}
