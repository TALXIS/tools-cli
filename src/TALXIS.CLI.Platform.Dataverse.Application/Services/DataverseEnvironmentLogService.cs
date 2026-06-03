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
}
