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
    /// <summary>Exit code: operation completed successfully.</summary>
    protected const int ExitSuccess = 0;
    /// <summary>Exit code: runtime/operational error.</summary>
    protected const int ExitError = 1;
    /// <summary>Exit code: input validation error or resource not found.</summary>
    protected const int ExitValidationError = 2;

    [CliOption(
        Name = "--format",
        Aliases = new[] { "-f" },
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
        var formatError = ApplyOutputFormat();
        if (formatError.HasValue)
            return formatError.Value;

        // Production guard runs first so users aren't prompted with --yes
        // only to be immediately blocked for missing --allow-production.
        try
        {
            var guardResult = await PreExecuteAsync().ConfigureAwait(false);
            if (guardResult.HasValue)
                return guardResult.Value;
        }
        catch (Exception ex) when (ex is Abstractions.ConfigurationResolutionException)
        {
            // If profile resolution fails in the guard, fall through — ExecuteAsync
            // will produce a better error message.
        }

        var confirmError = await CheckDestructiveConfirmationAsync().ConfigureAwait(false);
        if (confirmError.HasValue)
            return confirmError.Value;

        try
        {
            return await ExecuteAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is Abstractions.ConfigurationResolutionException or ArgumentException)
        {
            Logger.LogError("{Error}", ex.Message);
            return ExitValidationError;
        }
        catch (OperationCanceledException)
        {
            Logger.LogWarning("Operation was cancelled.");
            return ExitError;
        }
        catch (Exception ex)
        {
            // Log the message at Error (always visible) and the full exception
            // at Debug (only visible with --verbose / TXC_LOG_LEVEL=Debug).
            // This keeps the default terminal output clean while preserving
            // full diagnostics for troubleshooting.
            // Surface the innermost exception message when it differs from the
            // outer one — it typically contains the actionable root cause
            // (e.g. "Run 'txc config auth login' and retry.").
            var root = GetInnermostException(ex);
            if (root != ex && !string.Equals(root.Message, ex.Message, StringComparison.Ordinal))
            {
                Logger.LogError("Command failed: {Error}", ex.Message);
                Logger.LogError("Cause: {RootCause}", root.Message);
            }
            else
            {
                Logger.LogError("Command failed: {Error}", ex.Message);
            }
            Logger.LogDebug(ex, "Full exception details:");
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
