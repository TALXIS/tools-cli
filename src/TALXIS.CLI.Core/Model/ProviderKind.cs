namespace TALXIS.CLI.Core.Model;

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
