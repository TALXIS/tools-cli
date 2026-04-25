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
    Production = 0,
    /// <summary>Sandbox environment — safe for testing, no destructive guard.</summary>
    Sandbox = 1,
    /// <summary>Trial environment — time-limited, no destructive guard.</summary>
    Trial = 2,
    /// <summary>Developer environment — single-user dev, no destructive guard.</summary>
    Developer = 3,
    /// <summary>Default environment — auto-provisioned per tenant; treated as Production for safety.</summary>
    Default = 4,
}
