using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Xrm.Sdk.Query;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Logging;
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

            var solutions = await TryPlanSolutionsAsync(request, ct).ConfigureAwait(false);

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
                InteractiveAuthRequired: false,
                Solutions: solutions);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(request.TempWorkingDirectory))
            {
                LegacyAssemblyHostSubprocess.TryDeleteDirectory(request.TempWorkingDirectory);
            }
        }
    }

    private static async Task<IReadOnlyList<PackageSolutionImportPlan>?> TryPlanSolutionsAsync(PackageImportRequest request, CancellationToken ct)
    {
        try
        {
            var packageSolutions = new PackageImportConfigReader().ReadSolutionsInImportOrder(request.PackagePath);
            if (packageSolutions.Count == 0) return Array.Empty<PackageSolutionImportPlan>();

            using var conn = await DataverseCommandBridge.ConnectAsync(request.ProfileName, ct).ConfigureAwait(false);
            var query = new QueryExpression(DataverseSchema.Solution.EntityName)
            {
                ColumnSet = new ColumnSet("uniquename", "version"),
            };
            query.Criteria.AddCondition("uniquename", ConditionOperator.In, packageSolutions.Select(s => (object)s.UniqueName).ToArray());
            var response = await conn.Client.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);

            var installed = response.Entities.ToDictionary(
                e => e.GetAttributeValue<string>("uniquename"),
                e => (string?)e.GetAttributeValue<string>("version"),
                StringComparer.OrdinalIgnoreCase);
            return PackageSolutionImportPlanner.Plan(packageSolutions, installed);
        }
        catch (Exception ex)
        {
            // The comparison only adds detail to the result, so a failure here must not block the import.
            TxcLoggerFactory.CreateLogger(nameof(DataversePackageImportService))
                .LogWarning("Could not compare package solutions with the environment: {Error}", ex.Message);
            return null;
        }
    }
}
