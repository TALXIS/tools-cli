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
    /// <summary>Microsoft Teams-linked environment — backs a Teams team; not destructive by default.</summary>
    Teams = 5,
    /// <summary>Subscription-based trial environment — time-limited, convertible to production; no destructive guard.</summary>
    SubscriptionBasedTrial = 6,
}
