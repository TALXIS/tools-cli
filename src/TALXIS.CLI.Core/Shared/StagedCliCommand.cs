using System.ComponentModel;
using DotMake.CommandLine;

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
