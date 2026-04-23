using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.DependencyInjection;
using TALXIS.CLI.Config.Model;
using TALXIS.CLI.Config.Storage;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Shared;

namespace TALXIS.CLI.Config.Commands.Connection;

/// <summary>
/// <c>txc config connection create</c> — register a service endpoint.
/// Dataverse-only in v1 (the only provider with a live implementation);
/// other <see cref="ProviderKind"/> values return exit 1 with a clear
/// "not implemented in v1" message so future provider packages can plug
/// in without surface churn.
/// </summary>
[CliCommand(
    Name = "create",
    Description = "Create a connection (service endpoint). Dataverse-only in v1."
)]
public class ConnectionCreateCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(ConnectionCreateCliCommand));

    [CliArgument(Description = "Connection name.")]
    public required string Name { get; set; }

    [CliOption(Name = "--provider", Description = "Connection provider. Only 'dataverse' is supported in v1.", Required = true)]
    public ProviderKind Provider { get; set; }

    [CliOption(Name = "--environment", Aliases = new[] { "--url" }, Description = "Dataverse environment URL (required for --provider dataverse).", Required = false)]
    public string? EnvironmentUrl { get; set; }

    [CliOption(Name = "--cloud", Description = "Sovereign cloud for Dataverse. Default: public.", Required = false)]
    public CloudInstance? Cloud { get; set; }

    [CliOption(Name = "--organization-id", Aliases = new[] { "--org-id" }, Description = "Dataverse organization id (GUID). Optional.", Required = false)]
    public string? OrganizationId { get; set; }

    [CliOption(Name = "--tenant", Description = "Entra tenant id or domain. Optional — defaults to the credential's tenant at resolve time.", Required = false)]
    public string? TenantId { get; set; }

    [CliOption(Name = "--description", Description = "Free-form label shown in 'config connection list'.", Required = false)]
    public string? Description { get; set; }

    public async Task<int> RunAsync()
    {
        try
        {
            var name = Name?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                _logger.LogError("Connection name must not be empty.");
                return 1;
            }

            if (Provider != ProviderKind.Dataverse)
            {
                _logger.LogError(
                    "Provider '{Provider}' is not implemented in v1. Only 'dataverse' is supported.",
                    Provider);
                return 1;
            }

            if (string.IsNullOrWhiteSpace(EnvironmentUrl))
            {
                _logger.LogError("--environment <url> is required when --provider is 'dataverse'.");
                return 1;
            }
            if (!Uri.TryCreate(EnvironmentUrl, UriKind.Absolute, out var envUri)
                || (envUri.Scheme != Uri.UriSchemeHttp && envUri.Scheme != Uri.UriSchemeHttps))
            {
                _logger.LogError("--environment must be an absolute http(s) URL: '{Value}'.", EnvironmentUrl);
                return 1;
            }

            if (!string.IsNullOrWhiteSpace(OrganizationId) && !Guid.TryParse(OrganizationId, out _))
            {
                _logger.LogError("--organization-id must be a GUID: '{Value}'.", OrganizationId);
                return 1;
            }

            var store = TxcServices.Get<IConnectionStore>();
            var connection = new Model.Connection
            {
                Id = name,
                Provider = Provider,
                Description = Description,
                EnvironmentUrl = envUri.ToString().TrimEnd('/'),
                Cloud = Cloud ?? CloudInstance.Public,
                OrganizationId = OrganizationId,
                TenantId = TenantId,
            };
            await store.UpsertAsync(connection, CancellationToken.None).ConfigureAwait(false);

            _logger.LogInformation("Connection '{Name}' saved ({Provider} -> {Env}).",
                name, Provider, connection.EnvironmentUrl);

            OutputWriter.WriteLine(JsonSerializer.Serialize(
                new
                {
                    id = connection.Id,
                    provider = connection.Provider,
                    environmentUrl = connection.EnvironmentUrl,
                    cloud = connection.Cloud,
                    organizationId = connection.OrganizationId,
                    tenantId = connection.TenantId,
                    description = connection.Description,
                },
                new JsonSerializerOptions(TxcJsonOptions.Default)
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                }));
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create connection '{Name}'.", Name);
            return 1;
        }
    }
}
