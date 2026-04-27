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

    /// <summary>Leaf commands must declare [CliDestructive], [CliReadOnly], or [CliIdempotent]; [CliDestructive] commands must implement IDestructiveCommand.</summary>
    public const string MustDeclareAccessLevel = "TXC004";

    /// <summary>ExecuteAsync must not return raw integer literals — use ExitSuccess/ExitError/ExitValidationError.</summary>
    public const string NoRawIntegerReturn = "TXC005";

    /// <summary>ExecuteAsync must not contain try-catch blocks — the base class handles errors.</summary>
    public const string NoTryCatchInExecuteAsync = "TXC006";

    /// <summary>Leaf commands must not declare a --json CLI option — use the inherited --format flag.</summary>
    public const string NoJsonCliOption = "TXC007";

    /// <summary>Leaf commands must override the Logger property, not shadow it with a field.</summary>
    public const string MustOverrideLogger = "TXC008";

    /// <summary>Public enum members must have explicit integer values to prevent silent reordering breaks.</summary>
    public const string EnumMustHaveExplicitValues = "TXC009";

    /// <summary>Leaf [CliCommand] Description must be at least 20 characters — short descriptions degrade AI tool discovery.</summary>
    public const string DescriptionMinLength = "TXC010";

    /// <summary>ProfiledCliCommand subclass Description should mention "profile" or "environment" so the AI knows prerequisites.</summary>
    public const string ProfiledDescriptionContext = "TXC011";

    /// <summary>[CliDestructive] command Description should signal danger (delete, remove, uninstall, destructive, etc.).</summary>
    public const string DestructiveDescriptionSignal = "TXC012";

    /// <summary>Leaf command with ambiguous name should declare [CliWorkflow] to prevent workflow misclassification.</summary>
    public const string WorkflowRecommended = "TXC013";
}
