using TALXIS.CLI.Platform.Xrm;

namespace TALXIS.CLI.Platform.Dataverse.Application.Sdk;

/// <summary>
/// IPC envelope for the CMT data-export subprocess. Mirrors
/// <see cref="CmtImportJob"/>.
/// </summary>
public sealed record CmtExportJob(
    CmtExportRequest Request,
    string ProfileId,
    string? ConfigDirectory,
    int ParentProcessId);
