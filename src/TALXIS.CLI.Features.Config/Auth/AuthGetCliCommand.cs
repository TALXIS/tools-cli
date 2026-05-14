using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Config.Auth;

/// <summary>
/// <c>txc config auth show &lt;alias&gt;</c> — prints one credential's
/// non-secret fields as JSON, plus optional token health diagnostics.
/// Exit code 2 if the alias is not found so scripts can distinguish
/// "missing" from "internal error" (1).
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "get",
    Description = "Get a stored credential's non-secret fields as JSON."
)]
public class AuthGetCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(AuthGetCliCommand));

    [CliArgument(Description = "Credential alias (id).")]
    public required string Alias { get; set; }

    [CliOption(Name = "--check-token", Description = "Check token status and expiry; interactive re-authentication may occur for interactive credentials.", Required = false)]
    public bool CheckToken { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(Alias))
        {
            Logger.LogError("Credential alias must be provided.");
            return ExitError;
        }

        var store = TxcServices.Get<ICredentialStore>();
        var cred = await store.GetAsync(Alias, CancellationToken.None).ConfigureAwait(false);
        if (cred is null)
        {
            Logger.LogError("Credential '{Alias}' not found.", Alias);
            return ExitValidationError;
        }

        object? tokenDiagnostics = null;
        if (CheckToken)
        {
            tokenDiagnostics = await ProbeTokenHealthAsync(cred).ConfigureAwait(false);
        }

        var projected = new
        {
            id = cred.Id,
            kind = cred.Kind,
            tenantId = cred.TenantId,
            applicationId = cred.ApplicationId,
            cloud = cred.Cloud,
            description = cred.Description,
            audience = cred.Audience,
            scopes = cred.Scopes,
            certificatePath = cred.CertificatePath,
            secretRef = cred.SecretRef?.Uri,
            tokenHealth = tokenDiagnostics,
        };

        OutputFormatter.WriteData(projected);
        return ExitSuccess;
    }

    /// <summary>
    /// Attempts a silent token acquisition against a connection linked to this
    /// credential via a profile, and returns a diagnostic summary.
    /// </summary>
    private async Task<object> ProbeTokenHealthAsync(Credential cred)
    {
        try
        {
            var profileStore = TxcServices.Get<IProfileStore>();
            var connectionStore = TxcServices.Get<IConnectionStore>();
            var tokenService = TxcServices.Get<IAccessTokenService>();

            var profiles = await profileStore.ListAsync(CancellationToken.None).ConfigureAwait(false);

            // Find a profile that references this credential.
            var matchingProfile = profiles
                .FirstOrDefault(p => string.Equals(p.CredentialRef, cred.Id, StringComparison.OrdinalIgnoreCase));

            if (matchingProfile is null)
            {
                return new { status = "skipped", reason = "No profile found referencing this credential." };
            }

            var targetConnection = await connectionStore.GetAsync(matchingProfile.ConnectionRef, CancellationToken.None).ConfigureAwait(false);

            if (targetConnection is null || string.IsNullOrWhiteSpace(targetConnection.EnvironmentUrl))
            {
                return new { status = "skipped", reason = $"Profile '{matchingProfile.Id}' references connection '{matchingProfile.ConnectionRef}' which has no environment URL." };
            }

            if (!Uri.TryCreate(targetConnection.EnvironmentUrl, UriKind.Absolute, out var envUri))
            {
                return new { status = "skipped", reason = $"Connection '{targetConnection.Id}' has an invalid environment URL." };
            }

            var token = await tokenService.AcquireForResourceAsync(targetConnection, cred, envUri, CancellationToken.None).ConfigureAwait(false);

            // Decode the JWT expiry claim for diagnostics (basic parsing —
            // no signature validation needed for display purposes).
            var expiry = TryParseJwtExpiry(token);

            return new
            {
                status = "valid",
                profile = matchingProfile.Id,
                connection = targetConnection.Id,
                environmentUrl = targetConnection.EnvironmentUrl,
                expiresOn = expiry?.ToString("o"),
                expiresInMinutes = expiry.HasValue ? Math.Round((expiry.Value - DateTimeOffset.UtcNow).TotalMinutes, 1) : (double?)null,
            };
        }
        catch (Exception ex)
        {
            return new { status = "failed", error = ex.InnerException?.Message ?? ex.Message };
        }
    }

    /// <summary>
    /// Extracts the <c>exp</c> claim from a JWT access token without
    /// pulling in a full JWT library.
    /// </summary>
    private static DateTimeOffset? TryParseJwtExpiry(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return null;

            var payload = parts[1];
            // Pad Base64URL to standard Base64.
            payload = payload.Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }

            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("exp", out var expElement) && expElement.TryGetInt64(out var exp))
            {
                return DateTimeOffset.FromUnixTimeSeconds(exp);
            }
        }
        catch
        {
            // Best-effort diagnostics — JWT parsing failure is not critical.
        }
        return null;
    }
}
