using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Resolution;
using TALXIS.CLI.Core.Telemetry;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.MCP;

internal sealed class McpTelemetryEnricher
{
    private static readonly Microsoft.Extensions.Logging.ILogger Logger =
        TxcLoggerFactory.CreateLogger(nameof(McpTelemetryEnricher));

    private readonly ActivityIdentityTagger? _tagger;
    private readonly IProfileStore? _profiles;
    private readonly IConnectionStore? _connections;
    private readonly ICredentialStore? _credentials;
    private readonly IGlobalConfigStore? _globalConfig;
    private readonly IWorkspaceDiscovery? _workspaceDiscovery;

    public McpTelemetryEnricher(
        ActivityIdentityTagger? tagger,
        IProfileStore? profiles = null,
        IConnectionStore? connections = null,
        ICredentialStore? credentials = null,
        IGlobalConfigStore? globalConfig = null,
        IWorkspaceDiscovery? workspaceDiscovery = null)
    {
        _tagger = tagger;
        _profiles = profiles;
        _connections = connections;
        _credentials = credentials;
        _globalConfig = globalConfig;
        _workspaceDiscovery = workspaceDiscovery;
    }

    public Task TagActivityAsync(
        Activity? activity,
        IReadOnlyDictionary<string, JsonElement>? arguments,
        string? workingDirectory,
        CancellationToken ct)
    {
        if (activity == null)
            return Task.CompletedTask;

        return TagActivityCoreAsync(activity, arguments, workingDirectory, ct);
    }

    internal static string? TryGetProfile(IReadOnlyDictionary<string, JsonElement>? arguments)
    {
        if (arguments == null || !arguments.TryGetValue("profile", out var profileElement))
            return null;

        return profileElement.ValueKind == JsonValueKind.String
            ? profileElement.GetString()
            : null;
    }

    private async Task TagActivityCoreAsync(
        Activity activity,
        IReadOnlyDictionary<string, JsonElement>? arguments,
        string? workingDirectory,
        CancellationToken ct)
    {
        var profileName = TryGetProfile(arguments);

        try
        {
            if (!string.IsNullOrWhiteSpace(profileName))
            {
                if (_tagger != null)
                    await _tagger.TagFromProfileAsync(activity, profileName, ct).ConfigureAwait(false);
                return;
            }

            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                if (_tagger != null)
                    await _tagger.TagFromProfileAsync(activity, profileName: null, ct).ConfigureAwait(false);
                return;
            }

            if (_profiles == null || _connections == null || _credentials == null
                || _globalConfig == null || _workspaceDiscovery == null)
                return;

            var resolver = new ConfigurationResolver(
                _profiles,
                _connections,
                _credentials,
                _globalConfig,
                _workspaceDiscovery,
                new FixedCurrentDirectoryEnvironmentReader(workingDirectory));
            var context = await resolver.ResolveAsync(profileName: null, ct).ConfigureAwait(false);
            ActivityIdentityTagger.TagFromResolvedProfile(activity, context.Credential, context.Connection);
        }
        catch (Exception)
        {
            // Best-effort — telemetry enrichment must never fail the request path.
            Logger.LogDebug("Skipping MCP telemetry identity enrichment.");
        }
    }

    private sealed class FixedCurrentDirectoryEnvironmentReader : IEnvironmentReader
    {
        private readonly string _workingDirectory;

        public FixedCurrentDirectoryEnvironmentReader(string workingDirectory)
        {
            _workingDirectory = workingDirectory;
        }

        public string? Get(string name) => Environment.GetEnvironmentVariable(name);

        public string GetCurrentDirectory() => _workingDirectory;
    }
}
