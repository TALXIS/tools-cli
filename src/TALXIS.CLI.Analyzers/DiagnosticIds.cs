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

    /// <summary>Logger calls must use message templates, not string interpolation, to preserve structured data.</summary>
    public const string NoInterpolatedLogMessage = "TXC014";

    /// <summary>Catch blocks must not silently swallow exceptions — must log, rethrow, or return error.</summary>
    public const string NoBareExceptionSwallow = "TXC015";

    /// <summary>CreateLogger calls must use nameof() for consistent category names.</summary>
    public const string LoggerMustUseNameof = "TXC016";

    /// <summary>Do not create new ActivitySource instances — use TxcActivitySource.Instance.</summary>
    public const string NoNewActivitySource = "TXC017";

    /// <summary>Command classes should not call Environment.GetEnvironmentVariable — use DI/config.</summary>
    public const string NoEnvVarInCommands = "TXC018";

    /// <summary>Avoid DateTime.Now / DateTimeOffset.Now — prefer UTC or an injected clock.</summary>
    public const string NoRawDateTimeNow = "TXC019";

    /// <summary>Use TxcConstants instead of hardcoded repository URLs.</summary>
    public const string NoHardcodedRepoUrl = "TXC020";

    /// <summary>Use TxcActivitySource.CurrentOperationId instead of Activity.Current?.Id.</summary>
    public const string NoActivityCurrentId = "TXC021";

    /// <summary>GetInnermostException must only be defined in ExceptionHelpers.</summary>
    public const string NoDuplicateExceptionHelper = "TXC022";

    /// <summary>Prefer named interfaces over Func&lt;&gt;/Action&lt;&gt; in constructors.</summary>
    public const string NoFuncInConstructor = "TXC023";

    /// <summary>DI-injected classes should not access static singletons — inject them instead.</summary>
    public const string NoStaticSingletonInDiServices = "TXC024";

    /// <summary>Command code must not call Activity.SetTag/SetStatus/AddEvent/RecordException directly — use CommandActivityScope and ILogger.</summary>
    public const string NoDirectActivityTagging = "TXC025";

    /// <summary>Command code must not spawn processes directly — use CliSubprocessRunner or infrastructure.</summary>
    public const string NoDirectProcessStart = "TXC026";

    /// <summary>Mutative commands ([CliDestructive]/[CliIdempotent]) must call OutputFormatter.WriteResult() to produce a CommandResultEnvelope.</summary>
    public const string MustUseWriteResultForMutations = "TXC027";

    /// <summary>[CliReadOnly] commands must not call OutputFormatter.WriteResult() — use WriteData/WriteList/WriteDynamicTable for data output.</summary>
    public const string NoWriteResultInReadOnly = "TXC028";

    /// <summary>Do not construct CallToolResult directly — use McpToolResultFactory to ensure content/structuredContent consistency.</summary>
    public const string NoDirectCallToolResult = "TXC029";
}
