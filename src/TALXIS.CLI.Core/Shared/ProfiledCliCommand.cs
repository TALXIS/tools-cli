using System.Text.RegularExpressions;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Core;

/// <summary>
/// Base class for every leaf command that needs a resolved (Profile,
/// Connection, Credential) triple before it can run. Extends
/// <see cref="TxcLeafCommand"/> (which provides <c>--format</c> and
/// the standardized RunAsync/ExecuteAsync wrapper) and adds exactly two
/// CLI options — <c>--profile</c> (with its deliberate <c>-p</c> short
/// alias) and <c>--verbose</c>. The base class also provides <c>-f</c>
/// for <c>--format</c>.
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
/// <para>
/// Commands annotated with <see cref="CliDestructiveAttribute"/>
/// are blocked at runtime when the target environment is Production or
/// Default (or unknown), unless <c>--allow-production</c> is passed.
/// Detection uses both the API-reported <see cref="EnvironmentType"/> and
/// name-based heuristics (keywords in DisplayName / EnvironmentUrl).
/// </para>
/// </remarks>
public abstract class ProfiledCliCommand : TxcLeafCommand
{
    /// <summary>
    /// Case-insensitive pattern matching production-related keywords in
    /// environment display names and URL hostnames. Matches whole words
    /// or common abbreviations like <c>prd</c>, <c>prod</c>, <c>production</c>,
    /// <c>live</c>.
    /// </summary>
    private static readonly Regex ProductionNamePattern = new(
        @"\b(prod|production|prd|live)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

    [CliOption(
        Name = "--allow-production",
        Description = "Allow destructive operations against Production or Default environments.",
        Required = false)]
    public bool AllowProduction { get; set; }

    /// <summary>
    /// Returns <c>true</c> if the environment should be treated as production
    /// based on its API-reported type OR name-based heuristics.
    /// Null type (unknown) is treated as Production for fail-safe.
    /// </summary>
    public static bool IsProductionLike(EnvironmentType? envType, string? displayName = null, string? environmentUrl = null)
    {
        // API-reported type check.
        if (envType is null or EnvironmentType.Production or EnvironmentType.Default)
            return true;

        // Name-based heuristic: check display name and URL hostname for
        // production keywords even if the API type says Sandbox.
        if (!string.IsNullOrWhiteSpace(displayName) && ProductionNamePattern.IsMatch(displayName))
            return true;

        if (!string.IsNullOrWhiteSpace(environmentUrl) &&
            Uri.TryCreate(environmentUrl, UriKind.Absolute, out var uri) &&
            ProductionNamePattern.IsMatch(uri.Host))
            return true;

        return false;
    }

    /// <summary>
    /// Pre-execution guard: blocks destructive commands against production-like
    /// environments unless <c>--allow-production</c> is passed.
    /// </summary>
    protected override async Task<int?> PreExecuteAsync()
    {
        if (!Attribute.IsDefined(GetType(), typeof(CliDestructiveAttribute)))
            return null;

        try
        {
            var resolver = TxcServices.Get<IConfigurationResolver>();
            var context = await resolver.ResolveAsync(Profile, CancellationToken.None).ConfigureAwait(false);
            var connection = context.Connection;

            if (!IsProductionLike(connection.EnvironmentType, connection.DisplayName, connection.EnvironmentUrl))
                return null;

            if (AllowProduction)
            {
                Logger.LogWarning(
                    "Destructive operation allowed against {EnvType} environment '{ConnectionId}' ({Url}) via --allow-production.",
                    connection.EnvironmentType?.ToString() ?? "Unknown",
                    connection.Id,
                    connection.EnvironmentUrl);
                return null;
            }

            var envLabel = connection.DisplayName ?? connection.EnvironmentUrl ?? connection.Id;
            var typeLabel = connection.EnvironmentType?.ToString() ?? "Unknown (treated as Production)";
            Logger.LogError(
                "Blocked: this is a destructive operation targeting {EnvType} environment '{EnvLabel}'. " +
                "Pass --allow-production to confirm, or switch to a non-production profile.",
                typeLabel, envLabel);
            return ExitValidationError;
        }
        catch (ConfigurationResolutionException)
        {
            // If we can't resolve the profile, let ExecuteAsync handle it —
            // it will produce a better error message.
            return null;
        }
    }
}
