using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Setting;

/// <summary>
/// <c>txc environment setting list</c> — lists environment management
/// settings from the Power Platform control plane API.
/// </summary>
[CliCommand(
    Name = "list",
    Description = "List environment management settings (control plane)."
)]
public class SettingListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(SettingListCliCommand));

    [CliOption(Name = "--filter", Description = "Show only settings whose name contains this substring.", Required = false)]
    public string? Filter { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IEnvironmentManagementSettingsService>();
        IReadOnlyList<EnvironmentManagementSetting> settings = await service.ListAsync(Profile, selectFilter: null, CancellationToken.None)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(Filter))
        {
            settings = settings
                .Where(s => s.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // TODO: Refactor to use OutputFormatter instead of manual OutputContext.IsJson branching.
#pragma warning disable TXC003
        if (OutputContext.IsJson)
        {
            var dict = settings.ToDictionary(s => s.Name, s => s.Value);
            OutputWriter.WriteLine(JsonSerializer.Serialize(dict, TxcOutputJsonOptions.Default));
            return ExitSuccess;
        }

        PrintSettingsTable(settings);
        return ExitSuccess;
    }

    private static void PrintSettingsTable(IReadOnlyList<EnvironmentManagementSetting> settings)
    {
        if (settings.Count == 0)
        {
            OutputWriter.WriteLine("No environment management settings found.");
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
