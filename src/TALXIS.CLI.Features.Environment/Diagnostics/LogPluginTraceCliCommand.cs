using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Diagnostics;

/// <summary>
/// Reads plug-in execution traces from the <c>plugintracelog</c> table.
/// </summary>
/// <example>
///   txc environment log plugin-trace --since 24h --errors-only
///   txc env log plugin-trace --plugin MyCompany.Plugins.Account --entity account
/// </example>
[CliReadOnly]
[CliCommand(
    Name = "plugin-trace",
    Description = "Read plug-in execution traces from the LIVE environment. Requires plug-in trace logging to be enabled in the environment."
)]
public class LogPluginTraceCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(LogPluginTraceCliCommand));

    [CliOption(Name = "--since", Description = "Relative time window, e.g. 30m, 24h, 7d, 2w.", Required = false)]
    public string? Since { get; set; }

    [CliOption(Name = "--entity", Description = "Filter by primary entity logical name (e.g. account).", Required = false)]
    public string? Entity { get; set; }

    [CliOption(Name = "--plugin", Description = "Filter by plug-in / activity type-name (substring match).", Required = false)]
    public string? Plugin { get; set; }

    [CliOption(Name = "--errors-only", Description = "Only traces that recorded an exception.", Required = false)]
    public bool ErrorsOnly { get; set; }

    [CliOption(Name = "--correlation-id", Description = "Restrict to a single operation's correlation id (GUID).", Required = false)]
    public string? CorrelationId { get; set; }

    [CliOption(Name = "--top", Description = "Maximum number of traces to return.", Required = false)]
    public int? Top { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (!EnvLogFilterBuilder.TryBuild(Since, Entity, Plugin, ErrorsOnly, CorrelationId, Top, out var filter, out var error))
        {
            Logger.LogError("{Error}", error);
            return ExitValidationError;
        }

        var service = TxcServices.Get<IEnvironmentLogService>();
        var rows = await service.GetPluginTracesAsync(Profile, filter, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteList(rows, PrintTable);
        return ExitSuccess;
    }

    // Text-renderer callback invoked by OutputFormatter.WriteList — OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintTable(IReadOnlyList<PluginTraceRecord> rows)
    {
        if (rows.Count == 0)
        {
            OutputWriter.WriteLine("No plug-in traces found. (Is plug-in trace logging enabled in the environment?)");
            return;
        }

        int typeWidth = Math.Clamp(rows.Max(r => (r.TypeName ?? "").Length), 16, 44);
        int msgWidth = Math.Max(7, rows.Max(r => (r.MessageName ?? "").Length));
        int entityWidth = Math.Clamp(rows.Max(r => (r.PrimaryEntity ?? "").Length), 6, 24);

        string header = $"{"Created (UTC)",-19} | {"Lvl",-3} | {"Type".PadRight(typeWidth)} | {"Message".PadRight(msgWidth)} | {"Entity".PadRight(entityWidth)} | Detail";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));
        foreach (var r in rows)
        {
            string created = r.CreatedOnUtc?.ToString("yyyy-MM-dd HH:mm:ss") ?? "(unknown)";
            string level = r.HasException ? "ERR" : "ok";
            string type = LogText.Fit(r.TypeName, typeWidth);
            string msg = (r.MessageName ?? "").PadRight(msgWidth);
            string entity = LogText.Fit(r.PrimaryEntity, entityWidth);
            string detail = r.HasException ? (r.ExceptionSnippet ?? "") : (r.MessageSnippet ?? "");
            OutputWriter.WriteLine($"{created,-19} | {level,-3} | {type} | {msg} | {entity} | {detail}");
        }
        OutputWriter.WriteLine($"({rows.Count} trace{(rows.Count == 1 ? "" : "s")})");
    }
#pragma warning restore TXC003
}
