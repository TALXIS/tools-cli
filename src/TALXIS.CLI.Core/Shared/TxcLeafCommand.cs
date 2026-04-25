using DotMake.CommandLine;
using Microsoft.Extensions.Logging;

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
    /// Entry point called by the DotMake runtime (discovered by convention).
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

        try
        {
            return await ExecuteAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is Abstractions.ConfigurationResolutionException)
        {
            Logger.LogError("{Error}", ex.Message);
            return ExitError;
        }
        catch (OperationCanceledException)
        {
            Logger.LogWarning("Operation was cancelled.");
            return ExitError;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Command failed.");
            return ExitError;
        }
    }

    /// <summary>
    /// Implement command logic here. Return <see cref="ExitSuccess"/>,
    /// <see cref="ExitError"/>, or <see cref="ExitValidationError"/>.
    /// Unhandled exceptions are caught by the base and logged automatically.
    /// You may still catch domain-specific exceptions for custom exit codes.
    /// </summary>
    protected abstract Task<int> ExecuteAsync();

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
