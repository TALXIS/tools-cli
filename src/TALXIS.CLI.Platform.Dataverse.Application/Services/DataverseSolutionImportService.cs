using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using TALXIS.CLI.Platform.Dataverse.Runtime;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

internal sealed class DataverseSolutionImportService : ISolutionImportService
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(DataverseSolutionImportService));

    public async Task<SolutionImportResult> ImportAsync(
        string? profileName,
        string solutionZipPath,
        SolutionImportOptions options,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        var importer = new SolutionImporter(conn.Client, _logger);
        return await importer.ImportAsync(solutionZipPath, options).ConfigureAwait(false);
    }
}
