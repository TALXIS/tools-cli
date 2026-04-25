using Microsoft.Identity.Client;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using TALXIS.CLI.Platform.Dataverse.Runtime;
using TALXIS.CLI.Platform.Xrm;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

internal sealed class DataverseDataPackageService : IDataPackageService
{
    public async Task<DataPackageImportResult> ImportAsync(
        string? profileName,
        string dataPackagePath,
        int connectionCount,
        bool batchMode,
        int batchSize,
        bool overrideSafetyChecks,
        int prefetchLimit,
        bool deleteBeforeImport,
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

        var request = new CmtImportRequest(Path.GetFullPath(dataPackagePath), connectionCount, batchMode, batchSize, overrideSafetyChecks, prefetchLimit, deleteBeforeImport, verbose);
        CmtImportResult result = await LegacyAssemblyHostSubprocess
            .RunCmtImportAsync(request, profileName ?? string.Empty, ct)
            .ConfigureAwait(false);

        return new DataPackageImportResult(result.Succeeded, result.ErrorMessage, InteractiveAuthRequired: false);
    }

    public async Task<DataPackageExportResult> ExportAsync(
        string? profileName,
        string schemaPath,
        string outputPath,
        bool exportFiles,
        bool verbose,
        CancellationToken ct)
    {
        try
        {
            await DataverseCommandBridge.PrimeTokenAsync(profileName, ct).ConfigureAwait(false);
        }
        catch (MsalUiRequiredException)
        {
            return new DataPackageExportResult(false, null, InteractiveAuthRequired: true);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or NotSupportedException)
        {
            return new DataPackageExportResult(false, ex.Message, InteractiveAuthRequired: false);
        }

        var request = new CmtExportRequest(Path.GetFullPath(schemaPath), Path.GetFullPath(outputPath), exportFiles, verbose);
        CmtExportResult result = await LegacyAssemblyHostSubprocess
            .RunCmtExportAsync(request, profileName ?? string.Empty, ct)
            .ConfigureAwait(false);

        return new DataPackageExportResult(result.Succeeded, result.ErrorMessage, InteractiveAuthRequired: false);
    }
}
