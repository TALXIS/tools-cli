using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>
/// Creates a new global option set (choice) in Dataverse.
/// Usage: <c>txc environment entity optionset create-global --name &lt;schema-name&gt; --display-name &lt;label&gt; --options &lt;csv&gt; [--description &lt;text&gt;] [--solution &lt;name&gt;]</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "create-global",
    Description = "Create a new global option set (choice)."
)]
#pragma warning disable TXC003
public class EntityOptionSetCreateGlobalCliCommand : StagedCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EntityOptionSetCreateGlobalCliCommand));

    [CliOption(Name = "--name", Description = "The schema name of the global option set.", Required = true)]
    public string Name { get; set; } = null!;

    [CliOption(Name = "--display-name", Description = "The display name (label) for the option set.", Required = true)]
    public string DisplayName { get; set; } = null!;

    [CliOption(Name = "--options", Description = "Comma-separated options: \"Label1:100000000,Label2:100000001\" or \"Label1,Label2\" (auto-value).", Required = true)]
    public string Options { get; set; } = null!;

    [CliOption(Name = "--description", Description = "The description for the option set.", Required = false)]
    public string? Description { get; set; }

    [CliOption(Name = "--solution", Description = "The unique name of the solution to add the option set to.", Required = false)]
    public string? Solution { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        ValidateExecutionMode();

        if (Stage)
        {
            var store = TxcServices.Get<IChangesetStore>();
            store.Add(new StagedOperation
            {
                Category = "schema",
                OperationType = "CREATE",
                TargetType = "optionset",
                TargetDescription = Name,
                Details = $"display: \"{DisplayName}\", options: \"{Options}\"",
                Parameters = new Dictionary<string, object?>
                {
                    ["name"] = Name,
                    ["displayName"] = DisplayName,
                    ["options"] = Options,
                    ["description"] = Description,
                    ["solution"] = Solution
                }
            });
            OutputWriter.WriteLine($"Staged: CREATE global optionset '{Name}'");
            return ExitSuccess;
        }

        OptionMetadataInput[] parsed;
        try
        {
            parsed = ParseOptions(Options);
        }
        catch (FormatException ex)
        {
            Logger.LogError("{Error}", ex.Message);
            return ExitError;
        }

        try
        {
            var service = TxcServices.Get<IDataverseOptionSetService>();
            await service.CreateGlobalOptionSetAsync(
                Profile, Name, DisplayName, Description, parsed, Solution, CancellationToken.None
            ).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or ArgumentException)
        {
            Logger.LogError("{Error}", ex.Message);
            return ExitError;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "environment entity optionset create-global failed");
            return ExitError;
        }

        OutputWriter.WriteLine($"Global option set '{Name}' created successfully.");
        return ExitSuccess;
    }

    /// <summary>
    /// Parses a comma-separated options string into <see cref="OptionMetadataInput"/> items.
    /// Supports "Label:Value" pairs or plain "Label" (auto-valued starting at 100000000).
    /// </summary>
    internal static OptionMetadataInput[] ParseOptions(string csv)
    {
        var entries = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (entries.Length == 0)
            throw new FormatException("--options must contain at least one option.");

        var results = new OptionMetadataInput[entries.Length];
        int autoValue = 100_000_000;

        for (int i = 0; i < entries.Length; i++)
        {
            var parts = entries[i].Split(':', 2);
            if (parts.Length == 2)
            {
                if (!int.TryParse(parts[1].Trim(), out int v))
                    throw new FormatException($"Invalid option value '{parts[1].Trim()}' in '{entries[i]}'. Expected an integer.");
                results[i] = new OptionMetadataInput(parts[0].Trim(), v);
            }
            else
            {
                results[i] = new OptionMetadataInput(parts[0].Trim(), autoValue++);
            }
        }

        return results;
    }
}
