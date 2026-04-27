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
