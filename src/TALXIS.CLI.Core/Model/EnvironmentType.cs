namespace TALXIS.CLI.Core.Model;

/// <summary>
/// Power Platform environment lifecycle type. Determines whether
/// destructive operations require explicit confirmation. Values match
/// the <c>properties.environmentSku</c> field returned by the Power
/// Platform admin API.
/// </summary>
public enum EnvironmentType
{
    /// <summary>Full production environment — destructive operations are blocked by default.</summary>
    Production,
    /// <summary>Sandbox environment — safe for testing, no destructive guard.</summary>
    Sandbox,
    /// <summary>Trial environment — time-limited, no destructive guard.</summary>
    Trial,
    /// <summary>Developer environment — single-user dev, no destructive guard.</summary>
    Developer,
    /// <summary>Default environment — auto-provisioned per tenant; treated as Production for safety.</summary>
    Default,
}
