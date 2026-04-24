using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using TALXIS.CLI.Platform.Dataverse.Runtime;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

internal sealed class DataverseDeploymentHistoryService : IDeploymentHistoryService
{
    public async Task<DeploymentHistorySnapshot> GetRecentAsync(
        string? profileName,
        bool includePackages,
        bool includeSolutions,
        int maxCount,
        DateTime? sinceUtc,
        bool problemsOnly,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        var logger = TxcLoggerFactory.CreateLogger(nameof(DataverseDeploymentHistoryService));

        var pkgTask = includePackages
            ? new PackageHistoryReader(conn.Client, logger).GetRecentAsync(maxCount, sinceUtc, problemsOnly)
            : Task.FromResult<IReadOnlyList<PackageHistoryRecord>>(Array.Empty<PackageHistoryRecord>());
        var solTask = includeSolutions
            ? new SolutionHistoryReader(conn.Client, logger).GetRecentAsync(maxCount, sinceUtc, problemsOnly)
            : Task.FromResult<IReadOnlyList<SolutionHistoryRecord>>(Array.Empty<SolutionHistoryRecord>());

        await Task.WhenAll(pkgTask, solTask).ConfigureAwait(false);
        return new DeploymentHistorySnapshot(
            await pkgTask.ConfigureAwait(false),
            await solTask.ConfigureAwait(false));
    }
}
