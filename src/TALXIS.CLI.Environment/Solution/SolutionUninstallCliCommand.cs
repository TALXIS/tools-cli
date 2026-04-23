using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.Commands.Abstractions;
using TALXIS.CLI.Config.Providers.Dataverse.Runtime;
using TALXIS.CLI.Dataverse;
using TALXIS.CLI.Environment.Platforms.Dataverse;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Shared;

namespace TALXIS.CLI.Environment.Solution;

[CliCommand(
    Name = "uninstall",
    Description = "Uninstall a single solution by unique name from the target environment."
)]
public class SolutionUninstallCliCommand : ProfiledCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(SolutionUninstallCliCommand));

    [CliArgument(Name = "name", Description = "Solution unique name.", Required = true)]
    public required string Name { get; set; }

    [CliOption(Name = "--yes", Description = "Confirm destructive uninstall action.", Required = false)]
    public bool Yes { get; set; }

    [CliOption(Name = "--json", Description = "Emit uninstall result as JSON.", Required = false)]
    public bool Json { get; set; }

    public async Task<int> RunAsync()
    {
        if (!Yes)
        {
            _logger.LogError("Uninstall is destructive. Pass --yes to confirm.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            _logger.LogError("'name' argument is required.");
            return 1;
        }

        DataverseConnection conn;
        try
        {
            conn = await DataverseCommandBridge.ConnectAsync(Profile, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or NotSupportedException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }

        using (conn)
        {
            var client = conn.Client;
            try
            {
                var uninstaller = new SolutionUninstaller(client, _logger);
                var outcome = await uninstaller.UninstallByUniqueNameAsync(Name).ConfigureAwait(false);
                return RenderSingle(outcome);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "environment solution uninstall failed");
                return 1;
            }
        }
    }

    private int RenderSingle(SolutionUninstallOutcome outcome)
    {
        if (Json)
        {
            OutputWriter.WriteLine(JsonSerializer.Serialize(new
            {
                mode = "solution",
                outcome,
            }, JsonOptions));
        }
        else
        {
            OutputWriter.WriteLine($"Solution: {outcome.SolutionName}");
            OutputWriter.WriteLine($"  status: {outcome.Status}");
            if (outcome.SolutionId is { } id)
            {
                OutputWriter.WriteLine($"  id: {id}");
            }
            OutputWriter.WriteLine($"  message: {outcome.Message}");
        }

        return outcome.Status == SolutionUninstallStatus.Success ? 0 : 1;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
}
