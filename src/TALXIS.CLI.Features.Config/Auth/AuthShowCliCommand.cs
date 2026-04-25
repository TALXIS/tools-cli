using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Config.Auth;

/// <summary>
/// <c>txc config auth show &lt;alias&gt;</c> — prints one credential's
/// non-secret fields as JSON. Exit code 2 if the alias is not found so
/// scripts can distinguish "missing" from "internal error" (1).
/// </summary>
[McpToolAnnotations(ReadOnlyHint = true)]
[CliCommand(
    Name = "show",
    Description = "Show a stored credential's non-secret fields as JSON."
)]
public class AuthShowCliCommand : TxcLeafCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(AuthShowCliCommand));
    protected override ILogger Logger => _logger;

    [CliArgument(Description = "Credential alias (id).")]
    public required string Alias { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(Alias))
        {
            _logger.LogError("Credential alias must be provided.");
            return ExitError;
        }

        var store = TxcServices.Get<ICredentialStore>();
        var cred = await store.GetAsync(Alias, CancellationToken.None).ConfigureAwait(false);
        if (cred is null)
        {
            _logger.LogError("Credential '{Alias}' not found.", Alias);
            return ExitValidationError;
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

        OutputFormatter.WriteData(projected);
        return ExitSuccess;
    }
}
