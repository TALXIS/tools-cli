namespace TALXIS.CLI.Core.Model;

/// <summary>
/// Sovereign cloud instances recognised by txc. Maps to Entra authorities
/// in <see cref="TALXIS.CLI.Core.Identity.EntraCloudMap"/> and to Dataverse
/// URL inference in <c>TALXIS.CLI.Platform.Dataverse.Runtime.Authority.DataverseCloudMap</c>.
/// </summary>
public enum CloudInstance
{
    Public = 0,
    Gcc = 1,
    GccHigh = 2,
    Dod = 3,
    China = 4,
}
