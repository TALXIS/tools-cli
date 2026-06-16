using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Diagnostics;

/// <summary>
/// Parent command grouping runtime diagnostic log readers for a live environment
/// — plug-in traces, async jobs, classic workflow runs, and a unified feed.
/// </summary>
[CliCommand(
    Name = "log",
    Description = "Read runtime diagnostic logs from a live environment (plug-in traces, async jobs, workflow runs).",
    Children = new[]
    {
        typeof(LogListCliCommand),
        typeof(LogPluginTraceCliCommand),
        typeof(LogAsyncJobsCliCommand),
        typeof(LogWorkflowCliCommand),
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class LogCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
