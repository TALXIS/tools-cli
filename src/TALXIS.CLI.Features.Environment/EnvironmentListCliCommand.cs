using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment;

/// <summary>
/// <c>txc environment list</c> — lists the Power Platform environments in the
/// tenant visible to the active profile's identity. This is a tenant-level
/// admin operation: the profile supplies the credential and cloud, not a single
/// target environment.
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List the Power Platform environments in the tenant visible to the selected profile or auth credential."
)]
public class EnvironmentListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EnvironmentListCliCommand));

    [CliOption(Name = "--auth", Description = "Auth credential id to use directly when no target profile exists.", Required = false)]
    public string? Auth { get; set; }

    [CliOption(Name = "--cloud", Description = "Power Platform cloud to use with --auth or the default auth credential.", Required = false)]
    public CloudInstance? Cloud { get; set; }

    [CliOption(Name = "--filter", Description = "Show only environments whose display name, unique name, or URL contains this substring.", Required = false)]
    public string? Filter { get; set; }

    [CliOption(Name = "--type", Aliases = ["-t"], Description = "Show only environments of this lifecycle type (Production, Sandbox, Trial, Developer, Default, Teams, SubscriptionBasedTrial).", Required = false)]
    public EnvironmentType? Type { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IEnvironmentManagementService>();
        IReadOnlyList<EnvironmentInfo> environments = await service.ListAsync(Profile, Auth, Cloud, CancellationToken.None)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(Filter))
        {
            environments = environments
                .Where(e => Contains(e.DisplayName, Filter)
                    || Contains(e.UniqueName, Filter)
                    || Contains(e.EnvironmentUrl.ToString(), Filter))
                .ToList();
        }

        if (Type is { } type)
        {
            environments = environments.Where(e => e.EnvironmentType == type).ToList();
        }

        OutputFormatter.WriteList(environments, PrintTable);
        return ExitSuccess;
    }

    private static bool Contains(string? value, string substring)
        => value is not null && value.Contains(substring, StringComparison.OrdinalIgnoreCase);

    // Text-renderer callback invoked by OutputFormatter.WriteList — OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintTable(IReadOnlyList<EnvironmentInfo> environments)
    {
        if (environments.Count == 0)
        {
            OutputWriter.WriteLine("No environments found.");
            return;
        }

        int nameWidth = Math.Clamp(environments.Max(e => e.DisplayName.Length), 20, 40);
        int typeWidth = 12;
        string header = $"{"Display Name".PadRight(nameWidth)} | {"Type".PadRight(typeWidth)} | {"Environment ID".PadRight(36)} | Environment URL";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var e in environments.OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            string name = e.DisplayName.Length > nameWidth
                ? e.DisplayName[..(nameWidth - 1)] + "."
                : e.DisplayName;
            string type = (e.EnvironmentType?.ToString() ?? "Unknown");
            type = type.Length > typeWidth ? type[..typeWidth] : type;
            OutputWriter.WriteLine(
                $"{name.PadRight(nameWidth)} | {type.PadRight(typeWidth)} | {e.EnvironmentId.ToString().PadRight(36)} | {e.EnvironmentUrl}");
        }
    }
#pragma warning restore TXC003
}
