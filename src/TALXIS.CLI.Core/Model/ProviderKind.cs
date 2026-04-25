namespace TALXIS.CLI.Core.Model;

/// <summary>
/// Canonical connection providers. JSON serializes as kebab-case.
/// </summary>
public enum ProviderKind
{
    Dataverse = 0,
    Azure = 1,
    Ado = 2,
    Jira = 3,
}
