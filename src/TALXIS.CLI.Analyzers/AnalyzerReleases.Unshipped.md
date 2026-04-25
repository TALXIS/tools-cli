### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
TXC001 | TALXIS.CLI.Design | Error | Leaf [CliCommand] class must inherit TxcLeafCommand
TXC002 | TALXIS.CLI.Design | Error | Leaf commands must not define RunAsync()
TXC003 | TALXIS.CLI.Design | Error | Use OutputFormatter instead of OutputWriter in command code
TXC004 | TALXIS.CLI.Design | Error | Leaf commands must declare [CliDestructive], [CliReadOnly], or [CliIdempotent]
