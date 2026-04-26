using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Solution.Component;

[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List components in a solution."
)]
public class SolutionComponentListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(SolutionComponentListCliCommand));

    [CliArgument(Name = "solution", Description = "Solution unique name.")]
    public string SolutionName { get; set; } = null!;

    [CliOption(Name = "--type", Description = "Filter by component type (name or code, e.g. 'Entity' or '1').", Required = false)]
    public string? Type { get; set; }

    [CliOption(Name = "--entity", Description = "Filter by parent entity logical name.", Required = false)]
    public string? Entity { get; set; }

    [CliOption(Name = "--top", Description = "Maximum number of results to return.", Required = false)]
    public int? Top { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        int? typeFilter = null;
        if (!string.IsNullOrWhiteSpace(Type))
        {
            var resolver = new ComponentTypeResolver();
            if (!resolver.TryResolveCode(Type, out var code))
            {
                var known = string.Join(", ", resolver.GetKnownNames().Take(15));
            Logger.LogError("Unknown component type '{Type}'. Available types: {Known}. Or use an integer code.", Type, known);
                return ExitValidationError;
            }
            typeFilter = code;
        }

        var service = TxcServices.Get<ISolutionComponentQueryService>();
        var rows = await service.ListAsync(Profile, SolutionName, typeFilter, Entity, Top, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteList(rows, PrintComponentTable);
        return ExitSuccess;
    }

    // Text-renderer callback — OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintComponentTable(IReadOnlyList<ComponentSummaryRow> rows)
    {
        if (rows.Count == 0)
        {
            OutputWriter.WriteLine("No components found.");
            return;
        }

        int typeWidth = Math.Clamp(rows.Max(r => r.TypeName.Length), 10, 30);
        int nameWidth = Math.Clamp(rows.Max(r => (r.DisplayName ?? r.Name ?? "").Length), 15, 45);

        string header = $"{"Type".PadRight(typeWidth)} | {"Name".PadRight(nameWidth)} | {"ObjectId",-36} | Managed";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));
        foreach (var r in rows)
        {
            string type = r.TypeName.Length > typeWidth ? r.TypeName[..(typeWidth - 1)] + "." : r.TypeName;
            string name = (r.DisplayName ?? r.Name ?? "(unnamed)");
            if (name.Length > nameWidth)
                name = name[..(nameWidth - 1)] + ".";
            OutputWriter.WriteLine($"{type.PadRight(typeWidth)} | {name.PadRight(nameWidth)} | {r.ObjectId,-36} | {(r.Managed ? "yes" : "no")}");
        }
        OutputWriter.WriteLine($"\n{rows.Count} component(s) found.");
    }
#pragma warning restore TXC003
}
