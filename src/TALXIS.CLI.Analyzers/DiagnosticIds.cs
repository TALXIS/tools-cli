namespace TALXIS.CLI.Analyzers;

/// <summary>
/// Diagnostic IDs for the TALXIS CLI analyzer rules.
/// </summary>
internal static class DiagnosticIds
{
    /// <summary>Leaf [CliCommand] class must inherit TxcLeafCommand.</summary>
    public const string MustInheritTxcLeafCommand = "TXC001";

    /// <summary>Leaf commands must not define RunAsync() — base class owns it.</summary>
    public const string MustNotDefineRunAsync = "TXC002";

    /// <summary>Command code must not call OutputWriter directly — use OutputFormatter.</summary>
    public const string MustNotCallOutputWriter = "TXC003";

    /// <summary>Leaf commands must declare [CliDestructive] or [CliReadOnly] (mutually exclusive).</summary>
    public const string MustDeclareAccessLevel = "TXC004";
}
