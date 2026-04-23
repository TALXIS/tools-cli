using Microsoft.Identity.Client;
using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.Platforms.Dataverse;
using TALXIS.CLI.Config.Providers.Dataverse.Platforms;
using TALXIS.CLI.Config.Providers.Dataverse.Runtime;
using TALXIS.CLI.XrmTools;

namespace TALXIS.CLI.Config.Providers.Dataverse.Services;

internal sealed class DataverseDataPackageService : IDataPackageService
{
    public async Task<DataPackageImportResult> ImportAsync(
        string? profileName,
        string dataPackagePath,
        int connectionCount,
        bool verbose,
        CancellationToken ct)
    {
        try
        {
            await DataverseCommandBridge.PrimeTokenAsync(profileName, ct).ConfigureAwait(false);
        }
        catch (MsalUiRequiredException)
        {
            return new DataPackageImportResult(false, null, InteractiveAuthRequired: true);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or NotSupportedException)
        {
            return new DataPackageImportResult(false, ex.Message, InteractiveAuthRequired: false);
        }

        var request = new CmtImportRequest(Path.GetFullPath(dataPackagePath), connectionCount, verbose);
        CmtImportResult result = await LegacyAssemblyHostSubprocess
            .RunCmtImportAsync(request, profileName ?? string.Empty, ct)
            .ConfigureAwait(false);

        return new DataPackageImportResult(result.Succeeded, result.ErrorMessage, InteractiveAuthRequired: false);
    }
}
