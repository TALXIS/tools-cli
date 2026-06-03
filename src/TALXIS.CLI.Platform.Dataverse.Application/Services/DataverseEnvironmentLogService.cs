using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using TALXIS.CLI.Platform.Dataverse.Runtime;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

/// <summary>
/// Dataverse implementation of <see cref="IEnvironmentLogService"/>. Connects via
/// <see cref="DataverseCommandBridge"/> and delegates to the per-table readers.
/// </summary>
internal sealed class DataverseEnvironmentLogService : IEnvironmentLogService
{
    public async Task<IReadOnlyList<PluginTraceRecord>> GetPluginTracesAsync(
        string? profileName,
        EnvironmentLogFilter filter,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        var logger = TxcLoggerFactory.CreateLogger(nameof(DataverseEnvironmentLogService));
        return await new PluginTraceLogReader(conn.Client, logger).GetRecentAsync(filter, ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<AsyncJobRecord>> GetAsyncJobsAsync(
        string? profileName,
        EnvironmentLogFilter filter,
        CancellationToken ct) =>
        ReadAsyncJobsAsync(profileName, filter, operationTypeFilter: null, ct);

    public Task<IReadOnlyList<AsyncJobRecord>> GetWorkflowRunsAsync(
        string? profileName,
        EnvironmentLogFilter filter,
        CancellationToken ct) =>
        ReadAsyncJobsAsync(profileName, filter, DataverseSchema.AsyncOperation.OperationTypeWorkflow, ct);

    private static async Task<IReadOnlyList<AsyncJobRecord>> ReadAsyncJobsAsync(
        string? profileName,
        EnvironmentLogFilter filter,
        int? operationTypeFilter,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        var logger = TxcLoggerFactory.CreateLogger(nameof(DataverseEnvironmentLogService));
        return await new AsyncOperationReader(conn.Client, logger)
            .GetRecentAsync(filter, operationTypeFilter, ct).ConfigureAwait(false);
    }

    public async Task<IAsyncJobLogReader> CreateAsyncJobReaderAsync(
        string? profileName,
        int? operationTypeFilter,
        CancellationToken ct)
    {
        // Resolve + connect ONCE; the returned reader polls on this open connection.
        var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        return new AsyncJobLogReader(conn, operationTypeFilter);
    }

    /// <summary>
    /// Reader bound to a single open <see cref="DataverseConnection"/> so a
    /// <c>--follow</c> loop polls without re-resolving or re-connecting each tick.
    /// </summary>
    private sealed class AsyncJobLogReader : IAsyncJobLogReader
    {
        private readonly DataverseConnection _conn;
        private readonly AsyncOperationReader _reader;
        private readonly int? _operationTypeFilter;

        public AsyncJobLogReader(DataverseConnection conn, int? operationTypeFilter)
        {
            _conn = conn;
            _operationTypeFilter = operationTypeFilter;
            _reader = new AsyncOperationReader(conn.Client, TxcLoggerFactory.CreateLogger(nameof(AsyncJobLogReader)));
        }

        public Task<IReadOnlyList<AsyncJobRecord>> ReadAsync(EnvironmentLogFilter filter, CancellationToken ct)
            => _reader.GetRecentAsync(filter, _operationTypeFilter, ct);

        public void Dispose() => _conn.Dispose();
    }
}
