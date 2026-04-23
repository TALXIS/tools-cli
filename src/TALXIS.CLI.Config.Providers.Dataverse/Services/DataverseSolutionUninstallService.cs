using Microsoft.Extensions.Logging;
using TALXIS.CLI.Config.Platforms.Dataverse;
using TALXIS.CLI.Config.Providers.Dataverse.Platforms;
using TALXIS.CLI.Config.Providers.Dataverse.Runtime;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Config.Providers.Dataverse.Services;

internal sealed class DataverseSolutionUninstallService : ISolutionUninstallService
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(DataverseSolutionUninstallService));

    public async Task<SolutionUninstallOutcome> UninstallByUniqueNameAsync(
        string? profileName,
        string uniqueName,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        var uninstaller = new SolutionUninstaller(conn.Client, _logger);
        return await uninstaller.UninstallByUniqueNameAsync(uniqueName).ConfigureAwait(false);
    }
}
