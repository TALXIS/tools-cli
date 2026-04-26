using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Bootstrapping;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Config.Connection;

/// <summary>
/// <c>txc config connection create</c> — register a service endpoint.
/// Dataverse-only in v1 (the only provider with a live implementation);
/// other <see cref="ProviderKind"/> values return exit 1 with a clear
/// "not implemented in v1" message so future provider packages can plug
/// in without surface churn.
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "create",
    Description = "Create a connection (service endpoint). Dataverse-only in v1."
)]
public class ConnectionCreateCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectionCreateCliCommand));

    [CliArgument(Description = "Connection name.")]
    public required string Name { get; set; }

    [CliOption(Name = "--provider", Description = "Connection provider. Only 'dataverse' is supported in v1.", Required = true)]
    public ProviderKind Provider { get; set; }

    [CliOption(Name = "--environment", Aliases = ["--url"], Description = "Dataverse environment URL (required for --provider dataverse).", Required = false)]
    public string? EnvironmentUrl { get; set; }

    [CliOption(Name = "--cloud", Description = "Sovereign cloud for Dataverse. Default: public.", Required = false)]
    public CloudInstance? Cloud { get; set; }

    [CliOption(Name = "--organization-id", Aliases = ["--org-id"], Description = "Dataverse organization id (GUID). Optional.", Required = false)]
    public string? OrganizationId { get; set; }

    [CliOption(Name = "--environment-id", Aliases = ["--env-id"], Description = "Power Platform environment id (GUID) used by the control plane API. Optional — if omitted, it may be resolved later during `txc config profile create --url ...` bootstrap or at runtime when calling control-plane commands.", Required = false)]
    public string? EnvironmentId { get; set; }

    [CliOption(Name = "--tenant", Description = "Entra tenant id or domain. Optional — defaults to the credential's tenant at resolve time.", Required = false)]
    public string? TenantId { get; set; }

    [CliOption(Name = "--description", Description = "Free-form label shown in 'config connection list'.", Required = false)]
    public string? Description { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var svc = TxcServices.Get<ConnectionUpsertService>();
        var upsert = await svc.ValidateAndUpsertAsync(
            Name,
            Provider,
            EnvironmentUrl,
            Cloud,
            OrganizationId,
            EnvironmentId,
            TenantId,
            Description,
            CancellationToken.None).ConfigureAwait(false);

        if (upsert.Error is not null)
        {
            Logger.LogError("{Message}", upsert.Error);
            return ExitError;
        }

        var connection = upsert.Connection!;
        Logger.LogInformation("Connection '{Name}' saved ({Provider} -> {Env}).",
            connection.Id, connection.Provider, connection.EnvironmentUrl);

        OutputFormatter.WriteData(new
        {
            id = connection.Id,
            provider = connection.Provider,
            environmentUrl = connection.EnvironmentUrl,
            cloud = connection.Cloud,
            organizationId = connection.OrganizationId,
            environmentId = connection.EnvironmentId,
            tenantId = connection.TenantId,
            description = connection.Description,
        });
        return ExitSuccess;
    }
}
