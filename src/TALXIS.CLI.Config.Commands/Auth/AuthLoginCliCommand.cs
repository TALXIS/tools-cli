using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.DependencyInjection;
using TALXIS.CLI.Config.Headless;
using TALXIS.CLI.Config.Model;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Shared;

namespace TALXIS.CLI.Config.Commands.Auth;

/// <summary>
/// <c>txc config auth login</c> — eager interactive browser sign-in.
/// Persists an <see cref="CredentialKind.InteractiveBrowser"/>
/// credential whose refresh token sits in the shared MSAL cache; no
/// secret material is written to the txc credential-vault file.
/// </summary>
/// <remarks>
/// Fails fast with exit 1 in headless contexts — interactive browser is
/// never a permitted headless kind. See <see cref="HeadlessAuthRequiredException"/>.
/// </remarks>
[CliCommand(
    Name = "login",
    Description = "Interactive browser sign-in. Persists a credential named after the UPN (override with --alias)."
)]
public class AuthLoginCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(AuthLoginCliCommand));

    [CliOption(Name = "--tenant", Description = "Entra tenant id or domain. When omitted, the user picks an org in the browser.", Required = false)]
    public string? Tenant { get; set; }

    [CliOption(Name = "--alias", Description = "Credential alias. Default: signed-in UPN (collision-resolved).", Required = false)]
    public string? Alias { get; set; }

    [CliOption(Name = "--cloud", Description = "Sovereign cloud. Default: public.", Required = false)]
    public CloudInstance? Cloud { get; set; }

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        try
        {
            var headless = TxcServices.Get<IHeadlessDetector>();
            headless.EnsureKindAllowed(CredentialKind.InteractiveBrowser);

            var login = TxcServices.Get<IInteractiveLoginService>();
            var store = TxcServices.Get<ICredentialStore>();
            var cloud = Cloud ?? CloudInstance.Public;

            _logger.LogInformation("Starting interactive sign-in...");
            var result = await login.LoginAsync(Tenant, cloud, ct).ConfigureAwait(false);

            var alias = string.IsNullOrWhiteSpace(Alias)
                ? await ResolveDefaultAliasAsync(store, result.Upn, ct).ConfigureAwait(false)
                : Alias.Trim();

            var credential = new Credential
            {
                Id = alias,
                Kind = CredentialKind.InteractiveBrowser,
                TenantId = result.TenantId,
                Cloud = cloud,
                Description = $"Interactive sign-in ({result.Upn})",
            };
            await store.UpsertAsync(credential, ct).ConfigureAwait(false);

            _logger.LogInformation("Signed in as {Upn} (tenant {Tenant}). Credential '{Alias}' saved.",
                result.Upn, result.TenantId, alias);

            OutputWriter.WriteLine(JsonSerializer.Serialize(
                new { id = alias, upn = result.Upn, tenantId = result.TenantId, cloud },
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                }));
            return 0;
        }
        catch (HeadlessAuthRequiredException ex)
        {
            _logger.LogError("{Message}", ex.Message);
            return 1;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Interactive sign-in was cancelled.");
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Interactive sign-in failed.");
            return 1;
        }
    }

    /// <summary>
    /// Derives an alias from the UPN. Falls back to appending the UPN's
    /// tenant-domain short name, then numeric suffixes, until an unused
    /// alias is found. Exposed internally for unit testing.
    /// </summary>
    internal static async Task<string> ResolveDefaultAliasAsync(
        ICredentialStore store, string upn, CancellationToken ct)
    {
        var slug = upn.Trim().ToLowerInvariant();
        if (await store.GetAsync(slug, ct).ConfigureAwait(false) is null)
            return slug;

        var shortName = ExtractTenantShortName(upn);
        if (!string.IsNullOrEmpty(shortName))
        {
            var combined = $"{slug}-{shortName}";
            if (await store.GetAsync(combined, ct).ConfigureAwait(false) is null)
                return combined;
        }

        // Numeric fallback caps at 99 — past that the user should pass --alias.
        for (var i = 2; i < 100; i++)
        {
            var candidate = $"{slug}-{i}";
            if (await store.GetAsync(candidate, ct).ConfigureAwait(false) is null)
                return candidate;
        }

        throw new InvalidOperationException(
            $"Cannot derive a unique alias for '{upn}' — pass --alias explicitly.");
    }

    /// <summary>
    /// Returns the first domain label from the UPN in lowercase, e.g.
    /// <c>tomas@contoso.com</c> → <c>contoso</c>. Returns <c>null</c>
    /// when the UPN has no usable domain portion. Internal for tests.
    /// </summary>
    internal static string? ExtractTenantShortName(string upn)
    {
        if (string.IsNullOrWhiteSpace(upn)) return null;
        var at = upn.IndexOf('@');
        if (at < 0 || at == upn.Length - 1) return null;

        var domain = upn[(at + 1)..];
        var dot = domain.IndexOf('.');
        var head = dot > 0 ? domain[..dot] : domain;
        head = head.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(head) ? null : head;
    }
}
