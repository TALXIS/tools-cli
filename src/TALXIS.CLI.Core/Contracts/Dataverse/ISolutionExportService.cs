namespace TALXIS.CLI.Core.Contracts.Dataverse;

public sealed record SolutionExportOptions(
    string SolutionUniqueName,
    bool Managed,
    string OutputPath,
    bool Unpack);

public interface ISolutionExportService
{
    /// <summary>
    /// Exports a solution from the environment. When <see cref="SolutionExportOptions.Unpack"/>
    /// is true, the ZIP is unpacked via SolutionPackager into the output directory.
    /// Otherwise the raw ZIP is saved to the output path.
    /// </summary>
    Task<string> ExportAsync(string? profileName, SolutionExportOptions options, CancellationToken ct);
}
