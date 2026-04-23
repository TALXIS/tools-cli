using DotMake.CommandLine;

namespace TALXIS.CLI.Config.Commands.Abstractions;

/// <summary>
/// Base class for every leaf command that needs a resolved (Profile,
/// Connection, Credential) triple before it can run. Exposes exactly two
/// CLI options — <c>--profile</c> (with its deliberate <c>-p</c> short
/// alias, the only flag-level short in the CLI) and <c>--verbose</c>.
/// Everything else (endpoint URLs, credential material, device-code
/// toggles, env-var fallbacks) is resolved behind
/// <c>IConfigurationResolver</c>; leaf commands never parse raw auth
/// flags of their own.
/// </summary>
/// <remarks>
/// The options are declared as direct properties (not a nested options
/// record) so the MCP adapter — which reflects <c>[CliOption]</c>
/// properties on the command type — surfaces them automatically on every
/// derived command. The consistent two-flag surface is the whole point
/// of this refactor: agent prompts only have to know "pass
/// <c>--profile &lt;x&gt;</c> if you want a specific target" and every
/// command behaves identically.
/// </remarks>
public abstract class ProfiledCliCommand
{
    [CliOption(
        Name = "--profile",
        Aliases = new[] { "-p" },
        Description = "Profile name to resolve (falls back to TXC_PROFILE, workspace pin, or global active).",
        Required = false)]
    public string? Profile { get; set; }

    [CliOption(
        Name = "--verbose",
        Description = "Emit verbose logging for this invocation.",
        Required = false)]
    public bool Verbose { get; set; }
}
