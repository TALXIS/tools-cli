using Microsoft.Identity.Client;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using TALXIS.CLI.Platform.Dataverse.Runtime;
using TALXIS.CLI.Platform.Xrm;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

internal sealed class DataversePackageImportService : IPackageImportService
{
    public async Task<PackageImportResult> ImportAsync(PackageImportRequest request, CancellationToken ct)
    {
        try
        {
            try
            {
                await DataverseCommandBridge.PrimeTokenAsync(request.ProfileName, ct).ConfigureAwait(false);
            }
            catch (MsalUiRequiredException)
            {
                return new PackageImportResult(
                    Succeeded: false,
                    ErrorMessage: null,
                    LogFilePath: null,
                    CmtLogFilePath: null,
                    InteractiveAuthRequired: true);
            }

            var deploy = await LegacyAssemblyHostSubprocess.RunPackageDeployerAsync(new PackageDeployerRequest(
                request.PackagePath,
                ProfileId: request.ProfileName ?? string.Empty,
                ConfigDirectory: null,
                request.Settings,
                request.LogFile,
                request.LogConsole,
                request.Verbose,
                TemporaryArtifactsDirectory: string.Empty,
                ParentProcessId: System.Environment.ProcessId,
                NuGetPackageName: request.NuGetPackageName,
                NuGetPackageVersion: request.NuGetPackageVersion)).ConfigureAwait(false);

            return new PackageImportResult(
                Succeeded: deploy.Succeeded,
                ErrorMessage: deploy.ErrorMessage,
                LogFilePath: deploy.LogFilePath,
                CmtLogFilePath: deploy.CmtLogFilePath,
                InteractiveAuthRequired: false);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(request.TempWorkingDirectory))
            {
                LegacyAssemblyHostSubprocess.TryDeleteDirectory(request.TempWorkingDirectory);
            }
        }
    }
}
