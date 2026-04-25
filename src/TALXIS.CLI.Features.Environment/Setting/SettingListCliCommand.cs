using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Setting;

/// <summary>
/// <c>txc environment setting list</c> — lists environment settings from
/// all backends (control plane, Organization table, solution settings,
/// copilot governance).
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List environment settings."
)]
public class SettingListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(SettingListCliCommand));

    [CliOption(Name = "--filter", Description = "Show only settings whose name contains this substring.", Required = false)]
    public string? Filter { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IEnvironmentSettingsService>();
        IReadOnlyList<EnvironmentSetting> settings = await service.ListAsync(Profile, CancellationToken.None)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(Filter))
        {
            settings = settings
                .Where(s => s.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        OutputFormatter.WriteList(settings, PrintSettingsTable);
        return ExitSuccess;
    }

    // Text-renderer callback invoked by OutputFormatter.WriteList — OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintSettingsTable(IReadOnlyList<EnvironmentSetting> settings)
    {
        if (settings.Count == 0)
        {
            OutputWriter.WriteLine("No environment settings found.");
            return;
        }

        int nameWidth = Math.Clamp(settings.Max(s => s.Name.Length), 20, 60);
        string header = $"{"Setting".PadRight(nameWidth)} | Value";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length + 20));

        foreach (var s in settings.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            string name = s.Name.Length > nameWidth
                ? s.Name[..(nameWidth - 1)] + "."
                : s.Name;
            string value = s.Value?.ToString() ?? "(null)";
            OutputWriter.WriteLine($"{name.PadRight(nameWidth)} | {value}");
        }
    }
#pragma warning restore TXC003

}
