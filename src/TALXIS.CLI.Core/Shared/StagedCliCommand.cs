using System.ComponentModel;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;

namespace TALXIS.CLI.Core;

/// <summary>
/// Base class for mutating commands that support both immediate execution
/// (<c>--apply</c>) and local staging (<c>--stage</c>) for batch apply.
/// Inherits profile and verbose options from <see cref="ProfiledCliCommand"/>.
/// </summary>
public abstract class StagedCliCommand : ProfiledCliCommand
{
    [CliOption(
        Name = "--apply",
        Description = "Execute the operation immediately against the server.",
        Required = false)]
    [DefaultValue(false)]
    public bool Apply { get; set; }

    [CliOption(
        Name = "--stage",
        Description = "Add to a local changeset for batch apply later. Use 'txc environment changeset apply' to execute staged operations.",
        Required = false)]
    [DefaultValue(false)]
    public bool Stage { get; set; }

    /// <summary>Staging only edits a local changeset; every other mode reaches the environment.</summary>
    protected override bool ExecutesRemotely => Apply || !Stage;

    /// <summary>
    /// Refuses a run without exactly one of --apply or --stage before any profile is
    /// resolved, and says so on stdout as well, so a JSON caller sees that nothing ran.
    /// </summary>
    protected override int? ValidateInvocation()
    {
        if (Apply && Stage)
        {
            const string both = "Cannot specify both --apply and --stage. Choose one execution mode.";
            return Refuse("failed", both);
        }

        if (Apply || Stage) return null;

        var message = Attribute.IsDefined(GetType(), typeof(CliDestructiveAttribute))
            ? "Nothing deleted. Re-run with --apply to delete now, or with --stage to add it to the changeset."
            : "Nothing changed. Re-run with --apply to run it now, or with --stage to add it to the changeset.";
        return Refuse("not-applied", message);
    }

    private int Refuse(string status, string message)
    {
        OutputFormatter.WriteResult(status, message, exitCode: ExitValidationError);
        // The text result is already on stdout; JSON callers also get the structured log line.
        if (OutputContext.IsJson) Logger.LogError("{Error}", message);
        return ExitValidationError;
    }

    /// <summary>
    /// Validates that exactly one of --apply or --stage is specified.
    /// Call this at the beginning of ExecuteAsync() in derived commands.
    /// </summary>
    protected void ValidateExecutionMode()
    {
        if (!Apply && !Stage)
            throw new ArgumentException("You must specify either --apply (execute now) or --stage (add to changeset). Run with --help for details.");
        if (Apply && Stage)
            throw new ArgumentException("Cannot specify both --apply and --stage. Choose one execution mode.");
    }
}
