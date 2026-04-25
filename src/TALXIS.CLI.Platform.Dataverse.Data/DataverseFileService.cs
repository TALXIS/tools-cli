using System.Text;
using Microsoft.Xrm.Sdk;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Data;

/// <summary>
/// Implements file/image column operations against a live Dataverse
/// environment using the block-based upload/download SDK messages.
/// </summary>
internal sealed class DataverseFileService : IDataverseFileService
{
    /// <summary>Block size used for chunked transfers (4 MB).</summary>
    private const int BlockSize = 4 * 1024 * 1024;

    public async Task<string> DownloadFileAsync(
        string? profileName,
        string entityLogicalName,
        Guid recordId,
        string columnName,
        string outputPath,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        // 1. Initialize download
        var initRequest = new OrganizationRequest("InitializeFileBlocksDownload")
        {
            ["Target"] = new EntityReference(entityLogicalName, recordId),
            ["FileAttributeName"] = columnName
        };
        var initResponse = conn.Client.Execute(initRequest);
        var token = (string)initResponse["FileContinuationToken"];
        var fileSize = (long)initResponse["FileSizeInBytes"];
        var fileName = (string)initResponse["FileName"];

        // 2. Download in chunks
        using var output = File.Create(outputPath);
        for (long offset = 0; offset < fileSize; offset += BlockSize)
        {
            ct.ThrowIfCancellationRequested();
            var blockRequest = new OrganizationRequest("DownloadBlock")
            {
                ["Offset"] = offset,
                ["BlockLength"] = (long)BlockSize,
                ["FileContinuationToken"] = token
            };
            var blockResponse = conn.Client.Execute(blockRequest);
            var data = (byte[])blockResponse["Data"];
            output.Write(data);
        }

        return fileName;
    }

    public async Task UploadFileAsync(
        string? profileName,
        string entityLogicalName,
        Guid recordId,
        string columnName,
        string filePath,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        var localFileName = Path.GetFileName(filePath);

        // 1. Initialize upload
        var initRequest = new OrganizationRequest("InitializeFileBlocksUpload")
        {
            ["Target"] = new EntityReference(entityLogicalName, recordId),
            ["FileAttributeName"] = columnName,
            ["FileName"] = localFileName
        };
        var initResponse = conn.Client.Execute(initRequest);
        var token = (string)initResponse["FileContinuationToken"];

        // 2. Upload in chunks using streaming to avoid loading entire file into memory
        var blockIds = new List<string>();
        using var fileStream = File.OpenRead(filePath);
        var buffer = new byte[BlockSize];
        int bytesRead;
        while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            var blockId = Convert.ToBase64String(Encoding.UTF8.GetBytes(Guid.NewGuid().ToString()));
            blockIds.Add(blockId);

            var blockData = bytesRead == buffer.Length ? buffer : buffer[..bytesRead];
            var blockRequest = new OrganizationRequest("UploadBlock")
            {
                ["BlockId"] = blockId,
                ["BlockData"] = blockData,
                ["FileContinuationToken"] = token
            };
            conn.Client.Execute(blockRequest);
        }

        // 3. Commit
        var commitRequest = new OrganizationRequest("CommitFileBlocksUpload")
        {
            ["FileContinuationToken"] = token,
            ["FileName"] = localFileName,
            ["MimeType"] = "application/octet-stream",
            ["BlockList"] = blockIds.ToArray()
        };
        conn.Client.Execute(commitRequest);
    }
}
