using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment;

/// <summary>
/// <c>txc environment create</c> — provisions a new Power Platform environment
/// in the tenant. This is a tenant-level admin operation: the active profile
/// supplies the credential and cloud (admin authority), not a target
/// environment. By default the command returns once provisioning is queued;
/// pass <c>--wait</c> to block until the environment is ready.
/// </summary>
[CliIdempotent]
[CliLongRunning]
[CliCommand(
    Name = "create",
    Description = "Create a new Power Platform environment in the tenant. Requires an active profile (used for admin identity and cloud). Returns after queueing unless --wait is passed."
)]
public class EnvironmentCreateCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EnvironmentCreateCliCommand));

    [CliOption(Name = "--name", Aliases = ["-n"], Description = "Display name for the new environment. Required for every type except Teams.", Required = false)]
    public string? Name { get; set; }

    [CliOption(Name = "--type", Aliases = ["-t"], Description = "Environment lifecycle type: Production, Sandbox, Trial, Developer, Teams, or SubscriptionBasedTrial. (Default is not creatable.)", Required = true)]
    public EnvironmentType Type { get; set; }

    [CliOption(Name = "--region", Aliases = ["-r"], Description = "Azure geo region slug (e.g. unitedstates, europe, asia).", Required = false)]
    public string Region { get; set; } = "unitedstates";

    [CliOption(Name = "--currency", Aliases = ["-c"], Description = "ISO currency code, validated against the region's catalog.", Required = false)]
    public string Currency { get; set; } = "USD";

    [CliOption(Name = "--language", Aliases = ["-l"], Description = "Base language as an LCID (e.g. 1033) or a localized name (e.g. 'English (United States)').", Required = false)]
    public string Language { get; set; } = "1033";

    [CliOption(Name = "--domain", Aliases = ["-d"], Description = "Subdomain for the environment URL (2-32 chars). Defaults to a generated value when omitted.", Required = false)]
    public string? Domain { get; set; }

    [CliOption(Name = "--templates", Description = "Comma-separated Dynamics 365 app template names to provision (validated against the region/type catalog).", Required = false)]
    public string? Templates { get; set; }

    [CliOption(Name = "--security-group-id", Aliases = ["-sg"], Description = "Entra security group id that gates membership. Required for Teams environments.", Required = false)]
    public Guid? SecurityGroupId { get; set; }

    [CliOption(Name = "--user", Aliases = ["-u"], Description = "Owning user's Entra object id. Only valid for Developer environments.", Required = false)]
    public Guid? User { get; set; }

    [CliOption(Name = "--wait", Description = "Wait for provisioning to complete. By default the command returns after queueing.", Required = false)]
    public bool Wait { get; set; }

    [CliOption(Name = "--max-wait-minutes", Description = "Maximum minutes to wait when --wait is set (default 60).", Required = false)]
    public int MaxWaitMinutes { get; set; } = 60;

    protected override async Task<int> ExecuteAsync()
    {
        var templates = string.IsNullOrWhiteSpace(Templates)
            ? Array.Empty<string>()
            : Templates.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var options = new EnvironmentCreateOptions
        {
            DisplayName = Name,
            EnvironmentType = Type,
            Region = Region,
            CurrencyCode = Currency,
            Language = Language,
            DomainName = Domain,
            Templates = templates,
            SecurityGroupId = SecurityGroupId,
            UserObjectId = User,
            Wait = Wait,
            MaxWait = TimeSpan.FromMinutes(Math.Max(1, MaxWaitMinutes)),
        };

        var service = TxcServices.Get<IEnvironmentManagementService>();
        var result = await service.CreateAsync(Profile, options, CancellationToken.None).ConfigureAwait(false);

        if (result.Completed)
            Logger.LogInformation("Environment '{DisplayName}' provisioned ({EnvironmentId}).", result.DisplayName, result.EnvironmentId);
        else
            Logger.LogInformation("Environment creation queued ({EnvironmentId}); status {Status}.", result.EnvironmentId, result.Status);

        var payload = new
        {
            environmentId = result.EnvironmentId,
            displayName = result.DisplayName,
            environmentUrl = result.EnvironmentUrl?.ToString(),
            type = result.EnvironmentType?.ToString(),
            status = result.Status,
            completed = result.Completed,
            operationLocation = result.OperationLocation?.ToString(),
        };

        OutputFormatter.WriteData(payload, _ =>
        {
#pragma warning disable TXC003
            OutputWriter.WriteLine($"Environment ID: {result.EnvironmentId}");
            if (!string.IsNullOrWhiteSpace(result.DisplayName))
                OutputWriter.WriteLine($"Display Name: {result.DisplayName}");
            OutputWriter.WriteLine($"Type: {result.EnvironmentType?.ToString() ?? "Unknown"}");
            if (result.EnvironmentUrl is not null)
                OutputWriter.WriteLine($"Environment URL: {result.EnvironmentUrl}");
            OutputWriter.WriteLine($"Status: {result.Status}");
            if (!result.Completed)
                OutputWriter.WriteLine("Provisioning is in progress. Re-run 'txc env list' later to confirm, or pass --wait next time.");
#pragma warning restore TXC003
        });

        return ExitSuccess;
    }
}
