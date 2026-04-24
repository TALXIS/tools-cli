using TALXIS.CLI.Platform.Xrm;

namespace TALXIS.CLI.Platform.Dataverse.Application.Platforms;

/// <summary>
/// IPC envelope for the CMT data-import subprocess. Mirrors the shape of
/// <see cref="PackageDeployerRequest"/>'s IPC fields (profile/config/parent)
/// without polluting the lower-level <see cref="CmtImportRequest"/> record
/// (which is also used by in-process callers and tests that pass a
/// connection string).
/// </summary>
public sealed record CmtImportJob(
    CmtImportRequest Request,
    string ProfileId,
    string? ConfigDirectory,
    int ParentProcessId);
