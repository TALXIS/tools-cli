using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Core.Resolution;

/// <summary>
/// Default implementation of <see cref="IConfigurationResolver"/>.
/// Resolves the profile name using the 5-layer precedence documented in plan.md:
/// command-line &gt; <c>TXC_PROFILE</c> &gt; workspace file &gt; global active pointer
/// &gt; ephemeral (future: built from env vars by provider-specific builders).
/// Once a profile name is picked, the linked Connection and Credential are
/// loaded from the file-backed stores. Vault-held secret values are fetched
/// later, on demand, by providers.
/// </summary>
public sealed class ConfigurationResolver : IConfigurationResolver
{
    public const string ProfileEnvVar = "TXC_PROFILE";

    private readonly IProfileStore _profiles;
    private readonly IConnectionStore _connections;
    private readonly ICredentialStore _credentials;
    private readonly IGlobalConfigStore _globalConfig;
    private readonly IWorkspaceDiscovery _workspace;
    private readonly IEnvironmentReader _env;
    private readonly ILogger<ConfigurationResolver> _log;

    public ConfigurationResolver(
        IProfileStore profiles,
        IConnectionStore connections,
        ICredentialStore credentials,
        IGlobalConfigStore globalConfig,
        IWorkspaceDiscovery workspace,
        IEnvironmentReader? env = null,
        ILogger<ConfigurationResolver>? log = null)
    {
        _profiles = profiles;
        _connections = connections;
        _credentials = credentials;
        _globalConfig = globalConfig;
        _workspace = workspace;
        _env = env ?? ProcessEnvironmentReader.Instance;
        _log = log ?? NullLogger<ConfigurationResolver>.Instance;
    }

    public async Task<ResolvedProfileContext> ResolveAsync(string? profileName, CancellationToken ct)
    {
        var (name, source) = await PickProfileNameAsync(profileName, ct).ConfigureAwait(false);
        if (name is null)
        {
            throw new ConfigurationResolutionException(
                "No txc profile could be resolved. Pass --profile <name>, set TXC_PROFILE, "
                + "pin a workspace default with 'txc config profile pin', or select a global default with 'txc config profile select'.");
        }

        var profile = await _profiles.GetAsync(name, ct).ConfigureAwait(false)
            ?? throw new ConfigurationResolutionException(
                $"Profile '{name}' (source: {source}) was not found. Run 'txc config profile list' to see available profiles.");

        var connection = await _connections.GetAsync(profile.ConnectionRef, ct).ConfigureAwait(false)
            ?? throw new ConfigurationResolutionException(
                $"Profile '{profile.Id}' references missing connection '{profile.ConnectionRef}'.");

        var credential = await _credentials.GetAsync(profile.CredentialRef, ct).ConfigureAwait(false)
            ?? throw new ConfigurationResolutionException(
                $"Profile '{profile.Id}' references missing credential '{profile.CredentialRef}'.");

        _log.LogDebug("Resolved profile '{ProfileId}' (connection '{ConnectionId}', credential '{CredentialId}') from {Source}.",
            profile.Id, connection.Id, credential.Id, source);

        return new ResolvedProfileContext(profile, connection, credential, source);
    }

    private async Task<(string? name, ResolutionSource source)> PickProfileNameAsync(string? commandLineProfile, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(commandLineProfile))
            return (commandLineProfile, ResolutionSource.CommandLine);

        var envProfile = _env.Get(ProfileEnvVar);
        if (!string.IsNullOrWhiteSpace(envProfile))
            return (envProfile, ResolutionSource.EnvironmentVariable);

        var cwd = _env.GetCurrentDirectory();
        var workspace = await _workspace.DiscoverAsync(cwd, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(workspace?.Config.DefaultProfile))
            return (workspace!.Config.DefaultProfile, ResolutionSource.Workspace);

        var global = await _globalConfig.LoadAsync(ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(global.ActiveProfile))
            return (global.ActiveProfile, ResolutionSource.Global);

        return (null, ResolutionSource.Ephemeral);
    }
}
