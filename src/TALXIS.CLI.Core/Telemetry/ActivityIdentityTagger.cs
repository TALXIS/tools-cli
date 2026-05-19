using System.Diagnostics;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Core.Telemetry;

/// <summary>
/// Tags Activity spans with user/tenant/environment identity.
/// Constructor-injected via DI — host-agnostic, works for CLI (active profile),
/// MCP (subprocess context), and future REST API (auth context from HTTP headers).
/// </summary>
public sealed class ActivityIdentityTagger
{
    private readonly IGlobalConfigStore _configStore;
    private readonly IConfigurationResolver _resolver;

    public ActivityIdentityTagger(IGlobalConfigStore configStore, IConfigurationResolver resolver)
    {
        _configStore = configStore;
        _resolver = resolver;
    }

    /// <summary>
    /// Tags the Activity with identity from the globally active profile (if any).
    /// Best-effort: resolution failures are silently ignored — telemetry never blocks execution.
    /// </summary>
    public async Task TagFromActiveProfileAsync(Activity? activity)
    {
        if (activity == null) return;

        try
        {
            var config = await _configStore.LoadAsync(CancellationToken.None).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(config.ActiveProfile)) return;

            var context = await _resolver.ResolveAsync(null, CancellationToken.None).ConfigureAwait(false);
            TagFromResolvedProfile(activity, context.Credential, context.Connection);
        }
        catch (Exception) when (true)
        {
            // Best-effort — never block command execution for telemetry
        }
    }

    /// <summary>
    /// Tags the Activity with identity from an already-resolved profile.
    /// Called by <see cref="ProfiledCliCommand"/> when <c>--profile</c> is specified explicitly.
    /// </summary>
    public static void TagFromResolvedProfile(Activity? activity, Credential credential, Connection connection)
    {
        if (activity == null) return;

        var objectId = ExtractObjectId(credential.InteractiveAccountId)
            ?? credential.ApplicationId;
        if (!string.IsNullOrWhiteSpace(objectId))
        {
            activity.SetTag(TxcTelemetryTags.EndUserId, objectId);  // → App Insights built-in user_Id
            activity.SetTag(TxcTelemetryTags.UserId, objectId);     // → customDimensions for Kusto
        }

        var upn = credential.InteractiveUpn ?? credential.Id;
        if (!string.IsNullOrWhiteSpace(upn))
        {
            activity.SetTag(TxcTelemetryTags.EndUserName, upn);     // → App Insights built-in
            activity.SetTag(TxcTelemetryTags.UserName, upn);        // → customDimensions for Kusto
        }

        var tenantId = connection.TenantId ?? credential.TenantId;
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            activity.SetTag(TxcTelemetryTags.EndUserScope, tenantId); // → App Insights built-in
            activity.SetTag(TxcTelemetryTags.TenantId, tenantId);     // → customDimensions for Kusto
        }

        if (!string.IsNullOrWhiteSpace(connection.EnvironmentUrl))
            activity.SetTag(TxcTelemetryTags.EnvironmentUrl, connection.EnvironmentUrl);
        if (!string.IsNullOrWhiteSpace(connection.DisplayName))
            activity.SetTag(TxcTelemetryTags.EnvironmentName, connection.DisplayName);
    }

    /// <summary>
    /// Extracts the Entra object ID (first GUID) from MSAL's HomeAccountId.Identifier
    /// format: <c>{objectId}.{tenantId}</c>.
    /// </summary>
    private static string? ExtractObjectId(string? homeAccountId)
    {
        if (string.IsNullOrWhiteSpace(homeAccountId)) return null;
        var dot = homeAccountId.IndexOf('.');
        return dot > 0 ? homeAccountId[..dot] : homeAccountId;
    }
}
