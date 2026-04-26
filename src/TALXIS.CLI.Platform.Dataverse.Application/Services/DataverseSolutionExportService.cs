using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

/// <summary>
/// Exports a solution from Dataverse and optionally unpacks it via SolutionPackager.
/// </summary>
internal sealed class DataverseSolutionExportService : ISolutionExportService
{
    public async Task<string> ExportAsync(
        string? profileName,
        SolutionExportOptions options,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        var zipBytes = await SolutionExporter.ExportAsync(
            conn.Client, options.SolutionUniqueName, options.Managed, ct).ConfigureAwait(false);

        if (!options.Unpack)
        {
            // Save raw ZIP
            var zipPath = options.OutputPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                ? options.OutputPath
                : Path.Combine(options.OutputPath, $"{options.SolutionUniqueName}.zip");

            Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
            await File.WriteAllBytesAsync(zipPath, zipBytes, ct).ConfigureAwait(false);
            return zipPath;
        }

        // Save ZIP to temp, unpack, clean up
        var tempZip = Path.Combine(Path.GetTempPath(), $"txc_export_{Guid.NewGuid():N}.zip");
        try
        {
            await File.WriteAllBytesAsync(tempZip, zipBytes, ct).ConfigureAwait(false);

            Directory.CreateDirectory(options.OutputPath);
            SolutionPackagerService.Unpack(tempZip, options.OutputPath, options.Managed);

            return options.OutputPath;
        }
        finally
        {
            if (File.Exists(tempZip))
                File.Delete(tempZip);
        }
    }
}
