using System.Diagnostics;
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
/// name-based heuristics (keywords in DisplayName / EnvironmentUrl). When the
/// connection has no recorded type, the guard looks it up from the Power
/// Platform catalog (and persists it) before deciding; if that fails the
/// environment stays "unknown" and is treated as Production.
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

    /// <summary>Upper bound for the guard's catalog lookup of a missing environment type.</summary>
    private static readonly TimeSpan EnvironmentTypeLookupTimeout = TimeSpan.FromSeconds(20);

    [CliOption(
        Name = "--profile",
        Aliases = ["-p"],
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
        // Try to resolve the profile early to tag the Activity span with
        // user/tenant identity for App Insights correlation (OTel semantic
        // conventions: enduser.id, enduser.scope). This runs for ALL profiled
        // commands, not just destructive ones.
        ResolvedProfileContext? resolvedContext = null;
        ConfigurationResolutionException? resolutionFailure = null;
        try
        {
            var resolver = TxcServices.Get<IConfigurationResolver>();
            resolvedContext = await resolver.ResolveAsync(Profile, CancellationToken.None).ConfigureAwait(false);
            Telemetry.ActivityIdentityTagger.TagFromResolvedProfile(
                Activity.Current, resolvedContext.Credential, resolvedContext.Connection);
        }
        catch (ConfigurationResolutionException ex)
        {
            resolutionFailure = ex;
        }
        catch (Exception) when (true)
        {
            // If profile resolution fails (DI not bootstrapped, unreadable
            // store), let ExecuteAsync handle it — it will produce a better
            // error message. Identity tags are best-effort; we never block a
            // command for telemetry reasons.
        }

        if (!Attribute.IsDefined(GetType(), typeof(CliDestructiveAttribute)))
            return null;

        // Without a resolved profile there is no target to classify. Report the
        // resolution error here so the user sees the real problem (unknown
        // profile, nothing active) instead of a --yes/confirmation message
        // for a command that could never have run.
        if (resolutionFailure is not null)
        {
            Logger.LogError("{Error}", resolutionFailure.Message);
            return ExitValidationError;
        }

        if (resolvedContext is null)
            return null;

        var connection = resolvedContext.Connection;

        // Connections created without a catalog lookup (config connection
        // create, an earlier failed lookup, older versions) carry no type and
        // would be blocked as "Unknown". Ask the catalog once with the
        // credential we already resolved; the result is persisted. The guard
        // must not hang a command on a slow control plane, hence the timeout.
        if (connection.EnvironmentType is null)
        {
            using var cts = new CancellationTokenSource(EnvironmentTypeLookupTimeout);
            await EnvironmentTypeRefresher.TryRefreshAsync(connection, resolvedContext.Credential, Logger, cts.Token).ConfigureAwait(false);
        }

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

        var advice = connection.EnvironmentType is null
            ? "Run 'txc config profile validate --refresh-env-type' to record its type, or pass --allow-production to confirm."
            : "Pass --allow-production to confirm, or switch to a non-production profile.";
        return BlockProduction(connection.EnvironmentType, connection.DisplayName ?? connection.EnvironmentUrl ?? connection.Id, advice);
    }

    /// <summary>
    /// Logs the standard "blocked" error for a production-like target and
    /// returns the exit code. Shared by every guard so the wording stays uniform.
    /// </summary>
    protected int BlockProduction(EnvironmentType? envType, string envLabel, string advice)
    {
        Logger.LogError(
            "Blocked: this is a destructive operation targeting {EnvType} environment '{EnvLabel}'. {Advice}",
            envType?.ToString() ?? "Unknown (treated as Production)", envLabel, advice);
        return ExitValidationError;
    }
}
