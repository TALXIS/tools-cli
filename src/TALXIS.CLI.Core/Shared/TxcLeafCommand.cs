using System.Diagnostics;
using System.Reflection;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;

namespace TALXIS.CLI.Core;

/// <summary>
/// Base class for every leaf (non-routing) CLI command. Provides:
/// <list type="bullet">
///   <item><c>--format</c> (<c>-f</c>) option controlling JSON/text output (inherited by all leaves).</item>
///   <item>Automatic <see cref="OutputContext"/> setup from the <c>--format</c> flag with TTY auto-detection fallback.</item>
///   <item>Standardized error handling: <see cref="RunAsync"/> wraps <see cref="ExecuteAsync"/>
///         in a try/catch so leaf commands never need their own top-level exception guard.</item>
///   <item>Exit code contract: 0 = success, 1 = runtime error, 2 = input/validation error.</item>
/// </list>
/// <para>
/// Leaf commands implement <see cref="ExecuteAsync"/> instead of defining <c>RunAsync</c> directly.
/// The DotMake source generator discovers <see cref="RunAsync"/> on this base class and the
/// derived command inherits it automatically (see DotMake Command Inheritance docs).
/// </para>
/// <para>
/// Commands that also need a resolved (Profile, Connection, Credential) triple should
/// extend <c>ProfiledCliCommand</c> which inherits this class and adds <c>--profile</c>
/// and <c>--verbose</c> options.
/// </para>
/// </summary>
public abstract class TxcLeafCommand
{
    /// <summary>
    /// Shared ActivitySource for CLI command telemetry. Uses the same source name
    /// as <c>TxcTelemetry.Source</c> in the Logging project so a single
    /// TracerProvider listener captures both CLI and MCP spans.
    /// </summary>
    private static readonly ActivitySource TelemetrySource = new("TALXIS.CLI");

    /// <summary>Exit code: operation completed successfully.</summary>
    protected const int ExitSuccess = 0;
    /// <summary>Exit code: runtime/operational error.</summary>
    protected const int ExitError = 1;
    /// <summary>Exit code: input validation error or resource not found.</summary>
    protected const int ExitValidationError = 2;

    [CliOption(
        Name = "--format",
        Aliases = ["-f"],
        Description = "Output format: json (default when piped) or text (default in terminal).",
        Required = false)]
    public string? Format { get; set; }

    /// <summary>
    /// Logger instance for this command. Each leaf command must provide its own
    /// logger (typically via a field initializer calling <c>TxcLoggerFactory.CreateLogger</c>).
    /// The base class uses it for standardized error logging in the catch blocks.
    /// </summary>
    protected abstract ILogger Logger { get; }

    /// <summary>
    /// Entry point called by the DotMake runtime (resolved by convention).
    /// Sets up the output context from the <c>--format</c> flag, then delegates
    /// to <see cref="ExecuteAsync"/> inside a standardized try/catch.
    /// <para>
    /// <b>Do not override or hide this method in derived commands.</b>
    /// Implement <see cref="ExecuteAsync"/> instead.
    /// </para>
    /// </summary>
    public async Task<int> RunAsync()
    {
        var commandName = GetType().Name;

        // If spawned by MCP, adopt the parent trace context so this span
        // appears as a child of the MCP tool dispatch span in App Insights.
        var parentContext = RestoreParentTraceContext();
        // Server span — CLI command is the top-level "request" being served → App Insights 'requests' table
        using var activity = parentContext.HasValue
            ? TelemetrySource.StartActivity(commandName, ActivityKind.Server, parentContext.Value)
            : TelemetrySource.StartActivity(commandName, ActivityKind.Server);
        activity?.SetTag("txc.command", commandName);
        activity?.SetTag("txc.entry_point", "cli");
        activity?.SetTag("txc.version", GetCliVersion());

        // Best-effort: tag span with identity from the active profile so ALL
        // commands (not just ProfiledCliCommand) are attributable to a user.
        // ProfiledCliCommand.PreExecuteAsync overrides with the resolved profile
        // for commands that specify --profile explicitly.
        await TagActiveProfileIdentityAsync(activity).ConfigureAwait(false);

        var formatError = ApplyOutputFormat();
        if (formatError.HasValue)
        {
            activity?.SetTag("txc.exit_code", formatError.Value);
            return formatError.Value;
        }

        // Production guard runs first so users aren't prompted with --yes
        // only to be immediately blocked for missing --allow-production.
        try
        {
            var guardResult = await PreExecuteAsync().ConfigureAwait(false);
            if (guardResult.HasValue)
            {
                activity?.SetTag("txc.exit_code", guardResult.Value);
                return guardResult.Value;
            }
        }
        catch (Exception ex) when (ex is Abstractions.ConfigurationResolutionException)
        {
            // If profile resolution fails in the guard, fall through — ExecuteAsync
            // will produce a better error message.
        }

        var confirmError = await CheckDestructiveConfirmationAsync().ConfigureAwait(false);
        if (confirmError.HasValue)
        {
            activity?.SetTag("txc.exit_code", confirmError.Value);
            return confirmError.Value;
        }

        try
        {
            var exitCode = await ExecuteAsync().ConfigureAwait(false);
            activity?.SetTag("txc.exit_code", exitCode);
            if (exitCode != ExitSuccess)
                activity?.SetStatus(ActivityStatusCode.Error, $"Exit code {exitCode}");
            return exitCode;
        }
        catch (Exception ex) when (ex is Abstractions.ConfigurationResolutionException or ArgumentException)
        {
            // Structured properties flow to all ILogger providers including
            // TxcTelemetryLogProvider which maps txc.* to Activity tags and records
            // the exception in App Insights — no direct Activity calls needed here.
            LogCommandFailure(ex, ExitValidationError, "validation");
            return ExitValidationError;
        }
        catch (OperationCanceledException ex)
        {
            LogCommandFailure(ex, ExitError, "cancelled", LogLevel.Warning);
            return ExitError;
        }
        catch (Exception ex)
        {
            // Surface the innermost exception message — it typically contains the
            // actionable root cause (e.g. "Run 'txc config auth login' and retry.").
            var root = GetInnermostException(ex);
            var hasDistinctCause = root != ex && !string.Equals(root.Message, ex.Message, StringComparison.Ordinal);

            // Write structured error to stdout so scripts, pipes, and MCP get
            // a machine-readable error envelope instead of empty stdout.
            OutputFormatter.WriteResult("failed", hasDistinctCause ? root.Message : ex.Message);

            LogCommandFailure(ex, ExitError, "internal");
            if (hasDistinctCause)
                Logger.LogError("Cause: {RootCause}", root.Message);

            return ExitError;
        }
    }

    /// <summary>
    /// Pre-execution hook called before <see cref="ExecuteAsync"/>. Override
    /// in derived base classes to add cross-cutting guards (e.g. production
    /// environment protection). Return <c>null</c> to proceed, or an exit
    /// code to short-circuit.
    /// </summary>
    protected virtual Task<int?> PreExecuteAsync() => Task.FromResult<int?>(null);

    /// <summary>
    /// Implement command logic here. Return <see cref="ExitSuccess"/>,
    /// <see cref="ExitError"/>, or <see cref="ExitValidationError"/>.
    /// Unhandled exceptions are caught by the base and logged automatically.
    /// You may still catch domain-specific exceptions for custom exit codes.
    /// </summary>
    protected abstract Task<int> ExecuteAsync();

    /// <summary>
    /// Checks whether this command is a destructive operation (marked with
    /// <see cref="CliDestructiveAttribute"/> or implementing
    /// <see cref="IDestructiveCommand"/>) and enforces confirmation. In interactive
    /// terminals the user is prompted; in headless/CI the command fails unless
    /// <c>--yes</c> was passed. Returns an exit code to abort, or null to proceed.
    /// </summary>
    private async Task<int?> CheckDestructiveConfirmationAsync()
    {
        var destructiveAttr = GetType().GetCustomAttribute<CliDestructiveAttribute>();
        var dc = this as IDestructiveCommand;

        // [CliDestructive] is the source of truth; IDestructiveCommand provides --yes bypass.
        if (destructiveAttr is null && dc is null) return null;
        if (dc?.Yes == true) return null;

        var detector = TxcServices.Get<IHeadlessDetector>();
        if (detector.IsHeadless)
        {
            if (dc is null)
            {
                Logger.LogError(
                    "This operation is marked destructive and cannot run in non-interactive mode ({Reason}) because it does not expose --yes.",
                    detector.Reason);
            }
            else
            {
                Logger.LogError(
                    "This operation is destructive and requires --yes in non-interactive mode ({Reason}).",
                    detector.Reason);
            }

            return ExitValidationError;
        }

        var prompter = TxcServices.Get<IConfirmationPrompter>();
        var message = destructiveAttr?.Impact ?? "This operation is destructive.";

        if (!await prompter.ConfirmAsync($"{message} Continue?").ConfigureAwait(false))
        {
            Logger.LogWarning("Operation cancelled by user.");
            return ExitValidationError;
        }

        return null;
    }

    /// <summary>
    /// Logs a command failure with structured telemetry properties (<c>txc.exit_code</c>,
    /// <c>txc.error_kind</c>) that flow to all ILogger providers — console, JSON stderr,
    /// and the <c>TxcTelemetryLogProvider</c> which bridges them to Activity tags and
    /// App Insights exception recording. This keeps command code free of direct Activity calls.
    /// </summary>
    /// <summary>
    /// Logs a command failure and tags the current Activity span.
    /// The ILogger call carries the error message and exception to all providers
    /// (console, JSON stderr, telemetry). The Activity tags are span-level metadata
    /// set directly — they're infrastructure, not per-provider concerns.
    /// </summary>
    private void LogCommandFailure(Exception ex, int exitCode, string errorKind,
        LogLevel level = LogLevel.Error)
    {
        // Tag the Activity span with exit code and error classification.
        // These are span-level metadata (like txc.command, txc.entry_point)
        // set in RunAsync — keeping them here rather than in each catch block.
        var activity = Activity.Current;
        activity?.SetTag("txc.exit_code", exitCode);
        activity?.SetTag("txc.error_kind", errorKind);

        // Log through ILogger — TxcTelemetryLogProvider bridges the exception
        // to Activity.RecordException (→ App Insights exceptions table) and
        // sets Activity error status. Console/JSON providers render the message.
#pragma warning disable CA2254 // Log message template is intentionally dynamic for level routing
        Logger.Log(level, ex, "{Error}", ex.Message);
#pragma warning restore CA2254
    }

    /// <summary>
    /// Walks the <see cref="Exception.InnerException"/> chain and returns the
    /// deepest (innermost) exception. This is the one that usually contains the
    /// actionable root-cause message.
    /// </summary>
    private static Exception GetInnermostException(Exception ex)
    {
        while (ex.InnerException is not null)
            ex = ex.InnerException;
        return ex;
    }

    /// <summary>
    /// Tags the Activity with identity from the globally active profile (if any).
    /// This ensures even non-profiled commands like <c>config profile list</c> are
    /// attributable to a user when an active profile is set. Best-effort: any
    /// failure is silently ignored — telemetry never blocks command execution.
    /// </summary>
    private static async Task TagActiveProfileIdentityAsync(Activity? activity)
    {
        if (activity == null) return;

        try
        {
            var configStore = DependencyInjection.TxcServices.Get<Abstractions.IGlobalConfigStore>();
            var config = await configStore.LoadAsync(CancellationToken.None).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(config.ActiveProfile)) return;

            var resolver = DependencyInjection.TxcServices.Get<Abstractions.IConfigurationResolver>();
            var context = await resolver.ResolveAsync(null, CancellationToken.None).ConfigureAwait(false);

            var credential = context.Credential;
            var connection = context.Connection;

            // Extract Entra object ID from MSAL HomeAccountId ({objectId}.{tenantId})
            var objectId = ExtractObjectId(credential.InteractiveAccountId)
                ?? credential.ApplicationId;
            if (!string.IsNullOrWhiteSpace(objectId))
                activity.SetTag("enduser.id", objectId);

            var upn = credential.InteractiveUpn ?? credential.Id;
            if (!string.IsNullOrWhiteSpace(upn))
                activity.SetTag("user.name", upn);

            var tenantId = connection.TenantId ?? credential.TenantId;
            if (!string.IsNullOrWhiteSpace(tenantId))
                activity.SetTag("enduser.scope", tenantId);

            if (!string.IsNullOrWhiteSpace(connection.EnvironmentUrl))
                activity.SetTag("txc.environment_url", connection.EnvironmentUrl);
            if (!string.IsNullOrWhiteSpace(connection.DisplayName))
                activity.SetTag("txc.environment_name", connection.DisplayName);
        }
        catch (Exception) when (true)
        {
            // Best-effort — never block CLI for telemetry
        }
    }

    /// <summary>
    /// Extracts the Entra object ID (first GUID) from MSAL's HomeAccountId.Identifier
    /// format: <c>{objectId}.{tenantId}</c>. Returns null if the format is unexpected.
    /// </summary>
    private static string? ExtractObjectId(string? homeAccountId)
    {
        if (string.IsNullOrWhiteSpace(homeAccountId)) return null;
        var dot = homeAccountId.IndexOf('.');
        return dot > 0 ? homeAccountId[..dot] : homeAccountId;
    }

    private static string GetCliVersion()
    {
        // Use AssemblyVersion (e.g. "1.11.0") not InformationalVersion which
        // includes the git commit hash (e.g. "1.11.0+9fdf7ed...") and is ugly
        // in App Insights dashboards.
        return typeof(TxcLeafCommand).Assembly.GetName().Version?.ToString(3) ?? "unknown";
    }

    /// <summary>
    /// Reads <c>TXC_TRACEPARENT</c> environment variable (set by the MCP server when
    /// spawning CLI subprocesses) and returns a parent <see cref="ActivityContext"/>
    /// so this process's span becomes a child of the MCP tool dispatch span.
    /// Returns null for standalone CLI usage (no parent context).
    /// </summary>
    private static ActivityContext? RestoreParentTraceContext()
    {
        // W3C traceparent format: 00-{traceId 32 hex}-{spanId 16 hex}-{flags 2 hex}
        var traceparent = Environment.GetEnvironmentVariable("TXC_TRACEPARENT");
        if (string.IsNullOrEmpty(traceparent))
            return null;

        try
        {
            var parts = traceparent.Split('-');
            if (parts.Length < 4 || parts[1].Length != 32 || parts[2].Length != 16)
                return null;

            var traceId = ActivityTraceId.CreateFromString(parts[1].AsSpan());
            var spanId = ActivitySpanId.CreateFromString(parts[2].AsSpan());
            var flags = parts[3] == "01" ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None;
            return new ActivityContext(traceId, spanId, flags, isRemote: true);
        }
        catch
        {
            // Malformed traceparent — run as standalone (no parent)
            return null;
        }
    }

    /// <summary>
    /// Sets the ambient <see cref="OutputContext.Format"/> from the <c>--format</c>
    /// flag. When the flag is omitted, TTY auto-detection kicks in
    /// (JSON when stdout is redirected, text for interactive terminals).
    /// </summary>
    private int? ApplyOutputFormat()
    {
        // Reset any previously-set format from a prior invocation in the same
        // async flow (defensive against in-process hosting and unit tests).
        OutputContext.Reset();

        if (Format is not null)
        {
            if (Format.Equals("json", StringComparison.OrdinalIgnoreCase))
                OutputContext.Format = OutputFormat.Json;
            else if (Format.Equals("text", StringComparison.OrdinalIgnoreCase))
                OutputContext.Format = OutputFormat.Text;
            else
            {
                Logger.LogError("Unknown output format '{Format}'. Valid values: json, text.", Format);
                return ExitValidationError;
            }
        }
        return null;
    }
}
