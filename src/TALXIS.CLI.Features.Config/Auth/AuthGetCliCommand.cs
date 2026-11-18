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

    /// <summary>Tenant-level Power Platform admin (BAP) audience — same one <c>txc env list</c> uses.</summary>
    private static readonly Uri PowerPlatformAdminAudience = new("https://service.powerapps.com/");

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

        // Managed identity credentials are saved without tenant/app ids (the
        // endpoint decides the identity), so resolve them from a live token.
        var resolved = cred.Kind == CredentialKind.ManagedIdentity
            ? await ResolveManagedIdentityClaimsAsync(cred).ConfigureAwait(false)
            : null;

        object? tokenDiagnostics = null;
        if (CheckToken)
        {
            tokenDiagnostics = await ProbeTokenHealthAsync(cred).ConfigureAwait(false);
        }

        var projected = new
        {
            id = cred.Id,
            kind = cred.Kind,
            tenantId = cred.TenantId ?? resolved?.TenantId,
            applicationId = cred.ApplicationId ?? resolved?.ApplicationId,
            objectId = resolved?.ObjectId,
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
                // No environment to probe — fall back to the tenant-level
                // Power Platform admin API, which every supported kind can reach.
                var adminToken = await tokenService.AcquireForResourceAsync(
                    BuildTenantConnection(cred), cred, PowerPlatformAdminAudience, CancellationToken.None).ConfigureAwait(false);
                var adminExpiry = TryParseJwtExpiry(adminToken);
                return new
                {
                    status = "valid",
                    resource = PowerPlatformAdminAudience.ToString(),
                    expiresOn = adminExpiry?.ToString("o"),
                    expiresInMinutes = adminExpiry.HasValue ? Math.Round((adminExpiry.Value - DateTimeOffset.UtcNow).TotalMinutes, 1) : (double?)null,
                };
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
        using var doc = TryReadJwtPayload(token);
        if (doc is not null
            && doc.RootElement.TryGetProperty("exp", out var expElement)
            && expElement.TryGetInt64(out var exp))
        {
            return DateTimeOffset.FromUnixTimeSeconds(exp);
        }
        return null;
    }

    /// <summary>
    /// Acquires a token from the managed identity endpoint and reads the
    /// identity's tenant (<c>tid</c>), client id (<c>appid</c>/<c>azp</c>)
    /// and principal object id (<c>oid</c>). Display-only — nothing is
    /// persisted, because this command is read-only.
    /// </summary>
    private async Task<ManagedIdentityClaims?> ResolveManagedIdentityClaimsAsync(Credential cred)
    {
        try
        {
            var tokenService = TxcServices.Get<IAccessTokenService>();
            var token = await tokenService.AcquireForResourceAsync(
                BuildTenantConnection(cred), cred, PowerPlatformAdminAudience, CancellationToken.None).ConfigureAwait(false);

            using var doc = TryReadJwtPayload(token);
            if (doc is null) return null;
            return new ManagedIdentityClaims(
                ReadStringClaim(doc.RootElement, "tid"),
                ReadStringClaim(doc.RootElement, "appid") ?? ReadStringClaim(doc.RootElement, "azp"),
                ReadStringClaim(doc.RootElement, "oid"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Could not resolve managed identity details for '{Alias}': {Error}", cred.Id, ex.InnerException?.Message ?? ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Tenant-level connection used when no profile/environment is involved,
    /// mirroring the fallback used by <c>txc env list</c>.
    /// </summary>
    private static TALXIS.CLI.Core.Model.Connection BuildTenantConnection(Credential cred) => new()
    {
        Id = $"auth:{cred.Id}",
        Provider = ProviderKind.Dataverse,
        Cloud = cred.Cloud ?? CloudInstance.Public,
        TenantId = cred.TenantId,
    };

    private static string? ReadStringClaim(System.Text.Json.JsonElement payload, string name)
        => payload.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Decodes a JWT payload (no signature validation — display only).</summary>
    private static System.Text.Json.JsonDocument? TryReadJwtPayload(string token)
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
            return System.Text.Json.JsonDocument.Parse(json);
        }
        catch
        {
            // Best-effort diagnostics — JWT parsing failure is not critical.
            return null;
        }
    }

    private sealed record ManagedIdentityClaims(string? TenantId, string? ApplicationId, string? ObjectId);
}
