namespace TALXIS.CLI.Deploy;

/// <summary>
/// Static mappings for Dataverse option-set codes that appear in solution-history / import-job
/// rows. Kept in a separate type so tests can exercise the mapping without touching a live org.
/// </summary>
public static class SolutionHistoryMappings
{
    /// <summary>
    /// Maps <c>msdyn_solutionhistory.msdyn_suboperation</c> to a readable label.
    /// Codes: 1 = Install, 2 = HoldingImport, 3 = Update, 5 = Upgrade.
    /// </summary>
    public static string MapSuboperation(int? code) => code switch
    {
        1 => "Install",
        2 => "HoldingImport",
        3 => "Update",
        5 => "Upgrade",
        null => "Unknown",
        _ => $"Unknown({code})",
    };

    /// <summary>
    /// Maps <c>msdyn_solutionhistory.msdyn_operation</c> to a readable label.
    /// Commonly observed: 1 = Import, 2 = Uninstall.
    /// </summary>
    public static string MapOperation(int? code) => code switch
    {
        1 => "Import",
        2 => "Uninstall",
        null => "Unknown",
        _ => $"Unknown({code})",
    };
}
