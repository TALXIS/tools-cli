using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Bootstrapping;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Headless;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Storage;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Core;
using ConnectionModel = TALXIS.CLI.Core.Model.Connection;
using ProfileModel = TALXIS.CLI.Core.Model.Profile;

namespace TALXIS.CLI.Features.Config.Profile;

/// <summary>
/// <c>txc config profile create</c> — either a one-liner bootstrap from
/// a service URL (<c>--url</c>) or an explicit binding of an existing
/// credential to an existing connection (<c>--auth</c> + <c>--connection</c>).
///
/// <para>
/// The two modes are mutually exclusive and validated before any side
/// effect. The one-liner is the recommended onboarding path; the
/// primitive commands (<c>auth login</c>, <c>connection create</c>)
/// remain for advanced and non-interactive flows that the one-liner
/// cannot model (credential-only setup, one credential across many
/// connections, service-principal onboarding).
/// </para>
///
/// <para>
/// In <c>--url</c> mode the created profile is selected automatically unless
/// <c>--no-select</c> is passed. In explicit mode, the first profile created
/// is auto-promoted to the global active profile — same behaviour as before.
/// </para>
/// </summary>
[CliCommand(
    Name = "create",
    Description = "Create a profile. Quickstart: --url <env>. Advanced: --auth <alias> --connection <name>."
)]
public class ProfileCreateCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(ProfileCreateCliCommand));

    [CliOption(Name = "--name", Aliases = new[] { "-n" }, Description = "Profile name (slug). Optional — derived from the Power Platform environment name and URL host, or from --connection when omitted.", Required = false)]
    public string? Name { get; set; }

    [CliOption(Name = "--url", Description = "Service URL to bootstrap from. Triggers interactive sign-in, credential upsert, and connection creation in one step.", Required = false)]
    public string? Url { get; set; }

    [CliOption(Name = "--no-select", Description = "Do not make the created profile active. Used only with --url.", Required = false)]
    public bool NoSelect { get; set; }

    [CliOption(Name = "--provider", Description = "Connection provider. Optional in --url mode (inferred from the URL host).", Required = false)]
    public ProviderKind? Provider { get; set; }

    [CliOption(Name = "--tenant", Description = "Entra tenant id or domain. Forwarded to interactive sign-in when used with --url.", Required = false)]
    public string? Tenant { get; set; }

    [CliOption(Name = "--cloud", Description = "Sovereign cloud. Default: public. Used only with --url.", Required = false)]
    public CloudInstance? Cloud { get; set; }

    [CliOption(Name = "--auth", Description = "Existing credential alias (see 'config auth list'). Required in explicit mode.", Required = false)]
    public string? Auth { get; set; }

    [CliOption(Name = "--connection", Description = "Existing connection name (see 'config connection list'). Required in explicit mode.", Required = false)]
    public string? Connection { get; set; }

    [CliOption(Name = "--description", Description = "Free-form label shown in 'config profile list'.", Required = false)]
    public string? Description { get; set; }

    public async Task<int> RunAsync()
    {
        try
        {
            var mode = ClassifyMode();
            return mode switch
            {
                Mode.OneLiner => await RunOneLinerAsync().ConfigureAwait(false),
                Mode.Explicit => await RunExplicitAsync().ConfigureAwait(false),
                _ => LogUsageError(),
            };
        }
        catch (HeadlessAuthRequiredException ex)
        {
            _logger.LogError("{Message}", ex.Message);
            return 1;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Profile creation was cancelled.");
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create profile '{Name}'.", Name);
            return 1;
        }
    }

    private enum Mode { Invalid, OneLiner, Explicit }

    private Mode ClassifyMode()
    {
        var hasUrl = !string.IsNullOrWhiteSpace(Url);
        var hasAuth = !string.IsNullOrWhiteSpace(Auth);
        var hasConnection = !string.IsNullOrWhiteSpace(Connection);

        // Mixing --url with --auth/--connection is ambiguous on purpose: the
        // two modes write different primitives (one-liner *creates* a
        // credential; explicit *references* one).
        if (hasUrl && (hasAuth || hasConnection)) return Mode.Invalid;
        if (NoSelect && !hasUrl) return Mode.Invalid;
        if (hasUrl) return Mode.OneLiner;
        if (hasAuth && hasConnection) return Mode.Explicit;
        return Mode.Invalid;
    }

    private int LogUsageError()
    {
        _logger.LogError(
            "Specify either --url <service-url> [--no-select] (quickstart) or --auth <alias> --connection <name> (advanced). " +
            "Example: txc config profile create --url https://contoso.crm4.dynamics.com/");
        return 1;
    }

    private async Task<int> RunOneLinerAsync()
    {
        if (!IsAbsoluteHttpUrl(Url))
        {
            _logger.LogError("'{Url}' is not an absolute http(s) URL.", Url);
            return 1;
        }

        var inference = ProviderUrlResolver.Infer(Url);
        var provider = Provider ?? inference.Provider;
        if (provider is null)
        {
            _logger.LogError("{Message}", inference.Error);
            return 1;
        }
        if (Provider is not null && inference.Provider is not null && Provider != inference.Provider)
        {
            _logger.LogWarning(
                "Explicit --provider '{Explicit}' overrides URL-inferred '{Inferred}'.",
                Provider, inference.Provider);
        }

        var bootstrappers = TxcServices.GetAll<IConnectionProviderBootstrapper>();
        var bootstrapper = bootstrappers.FirstOrDefault(b => b.Provider == provider.Value);
        if (bootstrapper is null)
        {
            _logger.LogError(
                "No one-liner bootstrapper is registered for provider '{Provider}'. " +
                "Use the explicit --auth/--connection flow for this provider.",
                provider.Value);
            return 1;
        }

        var request = new ProfileBootstrapRequest(
            Name: string.IsNullOrWhiteSpace(Name) ? null : Name.Trim(),
            Provider: provider.Value,
            EnvironmentUrl: Url!,
            Cloud: Cloud,
            TenantId: Tenant,
            Description: Description);

        var result = await bootstrapper.BootstrapAsync(request, CancellationToken.None).ConfigureAwait(false);
        if (result.Error is not null)
        {
            _logger.LogError("{Message}", result.Error);
            return 1;
        }

        return await PersistProfileAsync(
            result.ProfileName,
            result.Credential!.Id,
            result.Connection.Id,
            result.Upn,
            NoSelect ? ProfileActivationMode.Never : ProfileActivationMode.Always).ConfigureAwait(false);
    }

    private async Task<int> RunExplicitAsync()
    {
        var profileStore = TxcServices.Get<IProfileStore>();
        var connectionStore = TxcServices.Get<IConnectionStore>();
        var credentialStore = TxcServices.Get<ICredentialStore>();

        var credential = await credentialStore.GetAsync(Auth!.Trim(), CancellationToken.None).ConfigureAwait(false);
        if (credential is null)
        {
            _logger.LogError("Credential '{Alias}' not found. Run 'txc config auth list'.", Auth);
            return 2;
        }

        var connection = await connectionStore.GetAsync(Connection!.Trim(), CancellationToken.None).ConfigureAwait(false);
        if (connection is null)
        {
            _logger.LogError("Connection '{Name}' not found. Run 'txc config connection list'.", Connection);
            return 2;
        }

        var name = string.IsNullOrWhiteSpace(Name) ? connection.Id : Name!.Trim();
        if (string.IsNullOrEmpty(name))
        {
            _logger.LogError("Profile name must not be empty.");
            return 1;
        }

        return await PersistProfileAsync(
            name,
            credential.Id,
            connection.Id,
            upn: null,
            ProfileActivationMode.IfMissing).ConfigureAwait(false);
    }

    private enum ProfileActivationMode
    {
        IfMissing,
        Always,
        Never,
    }

    private async Task<int> PersistProfileAsync(
        string name,
        string credentialId,
        string connectionId,
        string? upn,
        ProfileActivationMode activationMode)
    {
        var profileStore = TxcServices.Get<IProfileStore>();
        var globalConfig = TxcServices.Get<IGlobalConfigStore>();

        var profile = new ProfileModel
        {
            Id = name,
            ConnectionRef = connectionId,
            CredentialRef = credentialId,
            Description = Description,
        };

        await profileStore.UpsertAsync(profile, CancellationToken.None).ConfigureAwait(false);

        var global = await globalConfig.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        var activated = false;
        if (activationMode == ProfileActivationMode.Always)
        {
            global.ActiveProfile = profile.Id;
            await globalConfig.SaveAsync(global, CancellationToken.None).ConfigureAwait(false);
            activated = true;
            _logger.LogInformation("Profile '{Id}' is now the active profile.", profile.Id);
        }
        else if (activationMode == ProfileActivationMode.IfMissing && string.IsNullOrWhiteSpace(global.ActiveProfile))
        {
            global.ActiveProfile = profile.Id;
            await globalConfig.SaveAsync(global, CancellationToken.None).ConfigureAwait(false);
            activated = true;
            _logger.LogInformation("Profile '{Id}' is now the active profile.", profile.Id);
        }

        _logger.LogInformation(
            "Profile '{Id}' saved (auth='{Auth}', connection='{Connection}'{Upn}).",
            profile.Id, credentialId, connectionId,
            string.IsNullOrEmpty(upn) ? string.Empty : $", upn='{upn}'");

        OutputWriter.WriteLine(JsonSerializer.Serialize(
            new
            {
                id = profile.Id,
                connectionRef = profile.ConnectionRef,
                credentialRef = profile.CredentialRef,
                description = profile.Description,
                active = activated,
                upn,
            },
            TxcJsonOptions.Default));
        return 0;
    }

    private static bool IsAbsoluteHttpUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
