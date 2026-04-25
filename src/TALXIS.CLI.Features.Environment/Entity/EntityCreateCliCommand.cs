using System.ComponentModel;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>
/// Creates a new entity (table) in Dataverse.
/// Usage: <c>txc environment entity create --name &lt;schema-name&gt; --display-name &lt;label&gt; --plural-name &lt;label&gt; [--description &lt;text&gt;] [--solution &lt;name&gt;]</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "create",
    Description = "Create a new entity (table) in the environment."
)]
#pragma warning disable TXC003
public class EntityCreateCliCommand : StagedCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EntityCreateCliCommand));

    [CliOption(Name = "--name", Description = "The schema name of the new entity.", Required = true)]
    public string Name { get; set; } = null!;

    [CliOption(Name = "--display-name", Description = "The display name (label) for the entity.", Required = true)]
    public string DisplayName { get; set; } = null!;

    [CliOption(Name = "--plural-name", Description = "The plural display name (label) for the entity.", Required = true)]
    public string PluralName { get; set; } = null!;

    [CliOption(Name = "--description", Description = "The description for the entity.", Required = false)]
    public string? Description { get; set; }

    [CliOption(Name = "--solution", Description = "The unique name of the solution to add the entity to.", Required = false)]
    public string? Solution { get; set; }

    [CliOption(Name = "--ownership", Description = "Table ownership: 'user' (default) — records owned by users/teams; 'organization' — records owned by the org (no user-level access control).", Required = false)]
    [DefaultValue("user")]
    public string Ownership { get; set; } = "user";

    [CliOption(Name = "--type", Description = "Table type: 'standard' (default) — SQL-backed table; 'activity' — activity table with subject, dates, parties; 'elastic' — Azure Cosmos DB-backed for very large datasets.", Required = false)]
    [DefaultValue("standard")]
    public string TableType { get; set; } = "standard";

    [CliOption(Name = "--has-notes", Description = "Enable notes and file attachments on this table.", Required = false)]
    [DefaultValue(false)]
    public bool HasNotes { get; set; }

    [CliOption(Name = "--has-activities", Description = "Enable associating activities (emails, tasks, appointments) with records in this table.", Required = false)]
    [DefaultValue(false)]
    public bool HasActivities { get; set; }

    [CliOption(Name = "--enable-audit", Description = "Enable auditing for this table to track data changes.", Required = false)]
    [DefaultValue(false)]
    public bool EnableAudit { get; set; }

    [CliOption(Name = "--enable-change-tracking", Description = "Enable change tracking for data synchronization scenarios.", Required = false)]
    [DefaultValue(false)]
    public bool EnableChangeTracking { get; set; }

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
                TargetType = "entity",
                TargetDescription = Name,
                Details = $"display: \"{DisplayName}\", plural: \"{PluralName}\"",
                Parameters = new Dictionary<string, object?>
                {
                    ["name"] = Name,
                    ["displayName"] = DisplayName,
                    ["pluralName"] = PluralName,
                    ["description"] = Description,
                    ["solution"] = Solution,
                    ["ownership"] = Ownership,
                    ["tableType"] = TableType,
                    ["hasNotes"] = HasNotes,
                    ["hasActivities"] = HasActivities,
                    ["enableAudit"] = EnableAudit,
                    ["enableChangeTracking"] = EnableChangeTracking
                }
            });
            OutputWriter.WriteLine($"Staged: CREATE entity '{Name}'");
            return ExitSuccess;
        }

        var service = TxcServices.Get<IDataverseEntityMetadataService>();
        var options = new CreateEntityOptions
        {
            SchemaName = Name,
            DisplayName = DisplayName,
            PluralName = PluralName,
            Description = Description,
            Solution = Solution,
            Ownership = Ownership,
            TableType = TableType,
            HasNotes = HasNotes,
            HasActivities = HasActivities,
            EnableAudit = EnableAudit,
            EnableChangeTracking = EnableChangeTracking
        };
        await service.CreateEntityAsync(
            Profile, options, CancellationToken.None
        ).ConfigureAwait(false);

        OutputWriter.WriteLine($"Entity '{Name}' created successfully.");
        return ExitSuccess;
    }
}
