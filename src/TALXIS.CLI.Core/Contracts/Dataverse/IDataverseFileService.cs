namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// File/image column operations against a live Dataverse environment
/// using the block-based upload/download API.
/// </summary>
public interface IDataverseFileService
{
    /// <summary>
    /// Downloads a file/image column value to a local path.
    /// Returns the server-reported file name.
    /// </summary>
    Task<string> DownloadFileAsync(
        string? profileName,
        string entityLogicalName,
        Guid recordId,
        string columnName,
        string outputPath,
        CancellationToken ct);

    /// <summary>
    /// Uploads a local file to a file/image column on a record.
    /// </summary>
    Task UploadFileAsync(
        string? profileName,
        string entityLogicalName,
        Guid recordId,
        string columnName,
        string filePath,
        CancellationToken ct);
}
