using System.Diagnostics;
using Microsoft.Crm.Tools.SolutionPackager;
using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Platform.Dataverse.Application.Sdk;

/// <summary>
/// Wraps SolutionPackagerLib for packing and unpacking Dataverse solution ZIPs.
/// </summary>
internal sealed class SolutionPackagerServiceImpl : ISolutionPackagerService
{
    /// <summary>
    /// Unpacks a solution ZIP file into a folder structure.
    /// </summary>
    public void Unpack(string zipPath, string outputFolder, bool managed)
    {
        var arguments = new PackagerArguments
        {
            Action = CommandAction.Extract,
            PathToZipFile = zipPath,
            Folder = outputFolder,
            PackageType = managed ? SolutionPackageType.Managed : SolutionPackageType.Unmanaged,
            AllowDeletes = AllowDelete.Yes,
            AllowWrites = AllowWrite.Yes,
            ErrorLevel = TraceLevel.Info,
        };

        var packager = new SolutionPackager(arguments);
        packager.Run();
    }

    /// <summary>
    /// Packs a folder structure into a solution ZIP file.
    /// </summary>
    public void Pack(string folder, string zipPath, bool managed)
    {
        var arguments = new PackagerArguments
        {
            Action = CommandAction.Pack,
            PathToZipFile = zipPath,
            Folder = folder,
            PackageType = managed ? SolutionPackageType.Managed : SolutionPackageType.Unmanaged,
            ErrorLevel = TraceLevel.Info,
        };

        var packager = new SolutionPackager(arguments);
        packager.Run();
    }
}
