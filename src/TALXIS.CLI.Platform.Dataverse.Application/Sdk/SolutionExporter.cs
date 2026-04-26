using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;

namespace TALXIS.CLI.Platform.Dataverse.Application.Sdk;

/// <summary>
/// Exports a solution from Dataverse as a ZIP byte array.
/// </summary>
internal static class SolutionExporter
{
    public static async Task<byte[]> ExportAsync(
        IOrganizationServiceAsync2 service,
        string solutionUniqueName,
        bool managed,
        CancellationToken ct)
    {
        var request = new ExportSolutionRequest
        {
            SolutionName = solutionUniqueName,
            Managed = managed,
        };

        var response = (ExportSolutionResponse)
            await service.ExecuteAsync(request, ct).ConfigureAwait(false);

        return response.ExportSolutionFile;
    }
}
