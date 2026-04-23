using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.Bootstrapping;
using TALXIS.CLI.Config.DependencyInjection;
using TALXIS.CLI.Config.Headless;
using TALXIS.CLI.Config.Model;
using TALXIS.CLI.Config.Storage;
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
[McpIgnore]
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

    public async Task<int> RunAsync()
    {
        try
        {
            var headless = TxcServices.Get<IHeadlessDetector>();
            headless.EnsureKindAllowed(CredentialKind.InteractiveBrowser);

            var login = TxcServices.Get<IInteractiveLoginService>();
            var store = TxcServices.Get<ICredentialStore>();
            var cloud = Cloud ?? CloudInstance.Public;

            _logger.LogInformation("Starting interactive sign-in...");
            var result = await login.LoginAsync(Tenant, cloud, CancellationToken.None).ConfigureAwait(false);

            var alias = string.IsNullOrWhiteSpace(Alias)
                ? await CredentialAliasResolver.ResolveForUpnAsync(store, result.Upn, CancellationToken.None).ConfigureAwait(false)
                : Alias.Trim();

            var credential = new Credential
            {
                Id = alias,
                Kind = CredentialKind.InteractiveBrowser,
                TenantId = result.TenantId,
                Cloud = cloud,
                Description = $"Interactive sign-in ({result.Upn})",
            };
            await store.UpsertAsync(credential, CancellationToken.None).ConfigureAwait(false);

            _logger.LogInformation("Signed in as {Upn} (tenant {Tenant}). Credential '{Alias}' saved.",
                result.Upn, result.TenantId, alias);

            OutputWriter.WriteLine(JsonSerializer.Serialize(
                new { id = alias, upn = result.Upn, tenantId = result.TenantId, cloud },
                TxcJsonOptions.Default));
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
    /// Derives an alias from the UPN. Delegates to
    /// <see cref="CredentialAliasResolver.ResolveForUpnAsync"/>. Kept on
    /// the type for back-compat with existing tests and to surface the
    /// documentation next to the flag that wires it in.
    /// </summary>
    internal static Task<string> ResolveDefaultAliasAsync(
        ICredentialStore store, string upn, CancellationToken ct)
        => CredentialAliasResolver.ResolveForUpnAsync(store, upn, ct);

    /// <summary>
    /// Returns the first domain label from the UPN in lowercase, e.g.
    /// <c>tomas@contoso.com</c> → <c>contoso</c>. Delegates to
    /// <see cref="CredentialAliasResolver.ExtractTenantShortName"/>.
    /// </summary>
    internal static string? ExtractTenantShortName(string upn)
        => CredentialAliasResolver.ExtractTenantShortName(upn);
}
