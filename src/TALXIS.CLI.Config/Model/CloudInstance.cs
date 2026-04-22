namespace TALXIS.CLI.Config.Model;

/// <summary>
/// Sovereign cloud instances recognised by txc. Maps to Dataverse/Entra authorities
/// in <c>TALXIS.CLI.Config.Providers.Dataverse</c>.
/// </summary>
public enum CloudInstance
{
    Public,
    Gcc,
    GccHigh,
    Dod,
    China,
}
