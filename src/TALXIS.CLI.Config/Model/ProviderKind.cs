namespace TALXIS.CLI.Config.Model;

/// <summary>
/// Canonical connection providers. JSON serializes as kebab-case.
/// </summary>
public enum ProviderKind
{
    Dataverse,
    Azure,
    Ado,
    Jira,
}
