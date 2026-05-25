### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
TXC001 | TALXIS.CLI.Design | Error | Leaf [CliCommand] class must inherit TxcLeafCommand
TXC002 | TALXIS.CLI.Design | Error | Leaf commands must not define RunAsync()
TXC003 | TALXIS.CLI.Design | Error | Use OutputFormatter instead of OutputWriter in command code
TXC004 | TALXIS.CLI.Design | Error | Leaf commands must declare [CliDestructive], [CliReadOnly], or [CliIdempotent]
TXC005 | TALXIS.CLI.Design | Warning | Do not return raw integer literals from ExecuteAsync
TXC006 | TALXIS.CLI.Design | Warning | Do not use try-catch in ExecuteAsync
TXC007 | TALXIS.CLI.Design | Warning | Do not declare a --json CLI option
TXC008 | TALXIS.CLI.Design | Warning | Override the Logger property instead of declaring an ILogger field
TXC009 | TALXIS.CLI.Design | Warning | Public enum members must have explicit values
TXC010 | TALXIS.CLI.Design | Error | CLI command description must be at least 20 characters
TXC011 | TALXIS.CLI.Design | Warning | Profiled command description should mention profile or environment
TXC012 | TALXIS.CLI.Design | Warning | [CliDestructive] command description should signal danger
TXC013 | TALXIS.CLI.Design | Warning | Command with ambiguous name should declare [CliWorkflow]
TXC014 | TALXIS.CLI.Design | Warning | Use message templates instead of string interpolation in logger calls
TXC016 | TALXIS.CLI.Design | Warning | CreateLogger must use nameof() for the category name
TXC015 | TALXIS.CLI.Design | Warning | Catch blocks must not silently swallow exceptions
TXC017 | TALXIS.CLI.Design | Warning | Do not create new ActivitySource instances
TXC018 | TALXIS.CLI.Design | Info | Command classes should not read environment variables directly
TXC019 | TALXIS.CLI.Design | Info | Avoid DateTime.Now / DateTimeOffset.Now
TXC020 | TALXIS.CLI.Design | Warning | Use TxcConstants instead of hardcoded repository URLs
TXC021 | TALXIS.CLI.Design | Warning | Use TxcActivitySource.CurrentOperationId instead of Activity.Current.Id
TXC022 | TALXIS.CLI.Design | Warning | GetInnermostException must only be in ExceptionHelpers
TXC023 | TALXIS.CLI.Design | Info | Prefer named interfaces over Func/Action in constructors
TXC024 | TALXIS.CLI.Design | Info | DI-injected classes should not access static singletons
TXC025 | TALXIS.CLI.Design | Warning | Do not call Activity.SetTag/SetStatus/AddEvent in command code
TXC026 | TALXIS.CLI.Design | Warning | Do not spawn processes directly in command code
TXC027 | TALXIS.CLI.Design | Warning | Mutative commands must call OutputFormatter.WriteResult()
TXC028 | TALXIS.CLI.Design | Warning | [CliReadOnly] commands must not call OutputFormatter.WriteResult()
TXC029 | TALXIS.CLI.Design | Warning | Do not construct CallToolResult directly — use McpToolResultFactory
