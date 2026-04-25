using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Config.Abstractions;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Changeset;

/// <summary>
/// <c>txc environment changeset apply</c> — applies all staged operations
/// against the target Dataverse environment using the chosen execution strategy.
/// </summary>
[CliCommand(
    Name = "apply",
    Description = "Apply all staged changeset operations to the target environment."
)]
public class ChangesetApplyCliCommand : ProfiledCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(ChangesetApplyCliCommand));

    [CliOption(
        Name = "--strategy",
        Description = "Execution strategy for data operations: batch (ExecuteMultiple), transaction (ExecuteTransaction), or bulk (CreateMultiple/UpdateMultiple).",
        Required = true)]
    public string Strategy { get; set; } = null!;

    [CliOption(
        Name = "--continue-on-error",
        Description = "For batch strategy, continue processing remaining operations after a failure.",
        Required = false)]
    public bool ContinueOnError { get; set; }

    public async Task<int> RunAsync()
    {
        // Validate strategy value
        var validStrategies = new[] { "batch", "transaction", "bulk" };
        if (!validStrategies.Contains(Strategy, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogError("Invalid strategy '{Strategy}'. Valid values: batch, transaction, bulk.", Strategy);
            return 1;
        }

        var strategy = Strategy.ToLowerInvariant();

        var store = TxcServices.Get<IChangesetStore>();
        var operations = store.GetAll();

        if (operations.Count == 0)
        {
            OutputWriter.WriteLine("Changeset is empty. Nothing to apply.");
            return 0;
        }

        int schemaCount = operations.Count(o => o.Category == "schema");
        int dataCount = operations.Count(o => o.Category == "data");
        OutputWriter.WriteLine($"Applying {operations.Count} operations ({schemaCount} schema, {dataCount} data) with strategy '{strategy}'...");
        OutputWriter.WriteLine();

        try
        {
            var applier = TxcServices.Get<IChangesetApplier>();
            var result = await applier.ApplyAsync(
                Profile, operations, strategy, ContinueOnError, CancellationToken.None
            ).ConfigureAwait(false);

            // Print per-operation results
            foreach (var op in result.Results)
            {
                string status = op.Success ? "OK" : "FAIL";
                string line = $"  [{status}] #{op.Index}: {op.Message}";
                if (op.Error is not null)
                    line += $" — {op.Error}";
                OutputWriter.WriteLine(line);
            }

            OutputWriter.WriteLine();

            // Print summary
            OutputWriter.WriteLine($"Completed in {result.Duration.TotalSeconds:F1}s");
            OutputWriter.WriteLine($"  Total:      {result.TotalOperations}");
            OutputWriter.WriteLine($"  Succeeded:  {result.Succeeded}");
            OutputWriter.WriteLine($"  Failed:     {result.Failed}");
            if (result.Skipped > 0)
                OutputWriter.WriteLine($"  Skipped:    {result.Skipped}");
            if (result.RolledBack > 0)
                OutputWriter.WriteLine($"  Rolled back: {result.RolledBack}");

            // Clear the changeset store on full success
            if (result.Failed == 0)
            {
                store.Clear();
                OutputWriter.WriteLine();
                OutputWriter.WriteLine("Changeset cleared.");
            }

            return result.Failed > 0 ? 1 : 0;
        }
        catch (Exception ex) when (ex is Core.Abstractions.ConfigurationResolutionException or InvalidOperationException or ArgumentException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "environment changeset apply failed");
            return 1;
        }
    }
}
