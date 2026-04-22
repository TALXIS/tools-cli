using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.DependencyInjection;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Shared;

namespace TALXIS.CLI.Config.Commands.Auth;

/// <summary>
/// <c>txc config auth show &lt;alias&gt;</c> — prints one credential's
/// non-secret fields as JSON. Exit code 2 if the alias is not found so
/// scripts can distinguish "missing" from "internal error" (1).
/// </summary>
[CliCommand(
    Name = "show",
    Description = "Show a stored credential's non-secret fields as JSON."
)]
public class AuthShowCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(AuthShowCliCommand));

    [CliArgument(Description = "Credential alias (id).")]
    public required string Alias { get; set; }

    public async Task<int> RunAsync()
    {
        if (string.IsNullOrWhiteSpace(Alias))
        {
            _logger.LogError("Credential alias must be provided.");
            return 1;
        }

        try
        {
            var store = TxcServices.Get<ICredentialStore>();
            var cred = await store.GetAsync(Alias, CancellationToken.None).ConfigureAwait(false);
            if (cred is null)
            {
                _logger.LogError("Credential '{Alias}' not found.", Alias);
                return 2;
            }

            var projected = new
            {
                id = cred.Id,
                kind = cred.Kind,
                tenantId = cred.TenantId,
                applicationId = cred.ApplicationId,
                cloud = cred.Cloud,
                description = cred.Description,
                certificatePath = cred.CertificatePath,
                secretRef = cred.SecretRef?.Uri,
            };

            OutputWriter.WriteLine(JsonSerializer.Serialize(projected, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            }));
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show credential '{Alias}'.", Alias);
            return 1;
        }
    }
}
