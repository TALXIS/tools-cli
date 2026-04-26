namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Packs and unpacks Dataverse solution ZIPs using SolutionPackager.
/// No Dataverse connection required — operates on local files only.
/// </summary>
public interface ISolutionPackagerService
{
    void Pack(string folder, string zipPath, bool managed);
    void Unpack(string zipPath, string outputFolder, bool managed);
}
