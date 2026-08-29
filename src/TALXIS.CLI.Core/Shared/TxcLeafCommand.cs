using System.Diagnostics;
using System.Reflection;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Abstractions;
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
    /// <summary>Exit code: operation completed successfully.</summary>
    protected const int ExitSuccess = 0;
    /// <summary>Exit code: runtime/operational error.</summary>
    protected const int ExitError = 1;
    /// <summary>Exit code: input validation error or resource not found.</summary>
    protected const int ExitValidationError = 2;
    /// <summary>Exit code: authentication required — the user must sign in to the target environment before retrying.</summary>
    protected const int ExitAuthRequired = 3;

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
        var traceparent = Environment.GetEnvironmentVariable("TXC_TRACEPARENT");
        // When the CLI runs as an MCP subprocess, TXC_ENTRY_POINT=mcp is set by the
        // MCP server. Use it so all child spans consistently report the MCP entry point.
        var entryPoint = Environment.GetEnvironmentVariable("TXC_ENTRY_POINT")
            ?? Telemetry.TxcTelemetryTags.EntryPointCli;

        using var scope = new Telemetry.CommandActivityScope(
            commandName, entryPoint, traceparent);
        var exitCode = ExitError;

        try
        {
            // Best-effort: tag span with identity from the active profile so ALL
            // commands (not just ProfiledCliCommand) are attributable to a user.
            // ProfiledCliCommand.PreExecuteAsync overrides with the resolved profile.
            await TagActiveProfileIdentityAsync(scope.Activity).ConfigureAwait(false);

            var formatError = ApplyOutputFormat();
            if (formatError.HasValue)
                return exitCode = formatError.Value;

            // Production guard runs first so users aren't prompted with --yes
            // only to be immediately blocked for missing --allow-production.
            try
            {
                var guardResult = await PreExecuteAsync().ConfigureAwait(false);
                if (guardResult.HasValue)
                    return exitCode = guardResult.Value;
            }
            catch (Exception ex) when (ex is Abstractions.ConfigurationResolutionException)
            {
                // If profile resolution fails in the guard, fall through — ExecuteAsync
                // will produce a better error message.
            }

            var confirmError = await CheckDestructiveConfirmationAsync().ConfigureAwait(false);
            if (confirmError.HasValue)
                return exitCode = confirmError.Value;

            exitCode = await ExecuteAsync().ConfigureAwait(false);
            return exitCode;
        }
        catch (Exception ex) when (
            ExceptionHelpers.FindInChain<Headless.EnvironmentAuthRequiredException>(ex) is not null)
        {
            exitCode = ExitAuthRequired;

            // Surface the auth exception's own message, not the innermost MSAL error.
            var authEx = ExceptionHelpers.FindInChain<Headless.EnvironmentAuthRequiredException>(ex)!;

            scope.SetError(exitCode, "auth", authEx.Message);
            OutputFormatter.WriteResult("failed", authEx.Message, exitCode: exitCode);
            Logger.LogError(authEx, "Authentication required: {Error}", authEx.Message);
            LogSupportInfo();
            return exitCode;
        }
        catch (Exception ex) when (ex is Abstractions.ConfigurationResolutionException or ArgumentException)
        {
            exitCode = ExitValidationError;
            var root = ExceptionHelpers.GetInnermostException(ex);
            var message = root != ex && !string.Equals(root.Message, ex.Message, StringComparison.Ordinal)
                ? root.Message : ex.Message;
            scope.SetError(exitCode, "validation", message);
            Logger.LogError(ex, "{Error}", message);
            LogSupportInfo();
            return exitCode;
        }
        catch (OperationCanceledException ex)
        {
            exitCode = ExitError;
            scope.SetError(exitCode, "cancelled", "Operation was cancelled.");
            Logger.LogWarning(ex, "Operation was cancelled.");
            return exitCode;
        }
        catch (Exception ex)
        {
            exitCode = ExitError;

            var root = ExceptionHelpers.GetInnermostException(ex);
            var message = root != ex && !string.Equals(root.Message, ex.Message, StringComparison.Ordinal)
                ? root.Message : ex.Message;
            scope.SetError(exitCode, "internal", message);

            OutputFormatter.WriteResult("failed", message, exitCode: exitCode);
            Logger.LogError(ex, "Command failed: {Error}", message);
            LogSupportInfo();
            return exitCode;
        }
        finally
        {
            scope.SetExitCode(exitCode);
        }
    }

    /// <summary>
    /// Delegate that formats support escalation info (session ID, operation ID, GitHub link).
    /// Set once at startup by the entry-point layer (CLI/MCP) to avoid a Core → Logging dependency.
    /// When null, no support info is emitted.
    /// </summary>
    public static Func<string>? SupportInfoFormatter { get; set; }

    /// <summary>
    /// Logs support escalation info to stderr after an error.
    /// </summary>
    private void LogSupportInfo()
    {
        var info = SupportInfoFormatter?.Invoke();
        if (!string.IsNullOrEmpty(info))
            Logger.LogInformation("{SupportInfo}", info);
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
    /// Tags the Activity with identity from the globally active profile.
    /// Delegates to <see cref="Telemetry.ActivityIdentityTagger"/> resolved from DI.
    /// </summary>
    private static async Task TagActiveProfileIdentityAsync(Activity? activity)
    {
        var tagger = DependencyInjection.TxcServices.GetOptional<Telemetry.ActivityIdentityTagger>();
        if (tagger != null)
            await tagger.TagFromActiveProfileAsync(activity).ConfigureAwait(false);
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
