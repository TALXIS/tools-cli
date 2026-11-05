using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI;

/// <summary>
/// Reports command-line usage errors (unknown arguments, missing required options) as a
/// JSON result on stdout when JSON output is active, so callers parsing stdout see why it failed.
/// </summary>
public static class UsageErrorReporter
{
    /// <summary>
    /// Exit code for usage errors, matching <c>ExitValidationError</c> in the output contract.
    /// </summary>
    public const int ExitCode = 2;

    /// <summary>
    /// True when parsing failed and the parser would print the errors instead of running a command.
    /// </summary>
    public static bool HasUsageErrors(ParseResult parseResult)
        => parseResult.Errors.Count > 0 && parseResult.Action is ParseErrorAction;

    /// <summary>
    /// Writes the usage errors as a JSON result when JSON output is active and returns true;
    /// returns false in text mode, where the parser's own error output is kept.
    /// </summary>
    public static bool TryWriteJson(ParseResult parseResult, IReadOnlyList<string> args)
    {
        OutputContext.Reset();
        var format = ReadFormat(args);
        if (format is not null)
            OutputContext.Format = format.Value;
        if (!OutputContext.IsJson) return false;

        var message = BuildMessage(parseResult);
        OutputFormatter.WriteResult("failed", message, exitCode: ExitCode);

        var logger = TxcLoggerFactory.CreateLogger(nameof(UsageErrorReporter));
        logger.LogError("{Error}", message);
        return true;
    }

    /// <summary>
    /// Joins the parse errors and points at the help of the command that failed to parse.
    /// </summary>
    public static string BuildMessage(ParseResult parseResult)
    {
        var errors = string.Join(" ", parseResult.Errors.Select(e => e.Message).Distinct());
        return $"{errors} Run '{CommandPath(parseResult.CommandResult)} --help' for usage.";
    }

    /// <summary>
    /// Reads an explicit <c>--format</c> / <c>-f</c> value from raw arguments. Returns null when
    /// absent or not a known format, so the usual TTY auto-detection applies.
    /// </summary>
    public static OutputFormat? ReadFormat(IReadOnlyList<string> args)
    {
        string? value = null;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg == "--") break;
            if ((arg == "--format" || arg == "-f") && i + 1 < args.Count)
            {
                value = args[i + 1];
                continue;
            }

            foreach (var prefix in new[] { "--format=", "--format:", "-f=", "-f:" })
            {
                if (arg.StartsWith(prefix, StringComparison.Ordinal)) value = arg[prefix.Length..];
            }
        }

        if (string.Equals(value, "json", StringComparison.OrdinalIgnoreCase)) return OutputFormat.Json;
        if (string.Equals(value, "text", StringComparison.OrdinalIgnoreCase)) return OutputFormat.Text;
        return null;
    }

    private static string CommandPath(CommandResult commandResult)
    {
        var names = new List<string>();
        for (SymbolResult? current = commandResult; current is not null; current = current.Parent)
        {
            if (current is CommandResult command && command.Parent is not null) names.Insert(0, command.Command.Name);
        }

        names.Insert(0, "txc");
        return string.Join(" ", names);
    }
}
