using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Features.Config.Abstractions;
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
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(SettingListCliCommand));

    [CliOption(Name = "--filter", Aliases = new[] { "-f" }, Description = "Show only settings whose name contains this substring.", Required = false)]
    public string? Filter { get; set; }

    [CliOption(Name = "--json", Description = "Emit the list as indented JSON instead of a text table.", Required = false)]
    public bool Json { get; set; }

    public async Task<int> RunAsync()
    {
        IReadOnlyList<EnvironmentManagementSetting> settings;
        try
        {
            var service = TxcServices.Get<IEnvironmentManagementSettingsService>();
            settings = await service.ListAsync(Profile, selectFilter: null, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or NotSupportedException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "environment setting list failed");
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(Filter))
        {
            settings = settings
                .Where(s => s.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (Json)
        {
            var dict = settings.ToDictionary(s => s.Name, s => s.Value);
            OutputWriter.WriteLine(JsonSerializer.Serialize(dict, JsonOptions));
            return 0;
        }

        PrintSettingsTable(settings);
        return 0;
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

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
}
