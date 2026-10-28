using System.Text.Json;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;

/// <summary>
/// Settles a freshly written connection that still needs consent: completes it
/// silently when the credential and the connector allow, otherwise reports the
/// link a person must open.
/// </summary>
internal sealed class ConnectionConsentFlow
{
    private readonly SilentConsentHelper _silent;
    private readonly ConnectionReader _reader;

    public ConnectionConsentFlow(SilentConsentHelper silent, ConnectionReader reader)
    {
        _silent = silent ?? throw new ArgumentNullException(nameof(silent));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    /// <param name="written">The platform's answer to the write that left the connection unauthenticated.</param>
    /// <param name="attemptSilently">Whether the caller allows a silent attempt.</param>
    public async Task<ConnectionCreateOutcome> SettleAsync(
        ConnectivityTarget target,
        PendingConnection pending,
        JsonElement written,
        bool attemptSilently,
        CancellationToken ct)
    {
        var state = ConnectionJsonParser.ReadState(written);
        var consent = ConnectionJsonParser.ReadConsentInfo(written);

        if (consent.FirstPartyLoginUri is null)
            return RequiresPerson(pending, state, consent, "This connector needs a person to sign in; open the consent link.");
        if (!attemptSilently)
            return RequiresPerson(pending, state, consent, "Consent was not attempted.");

        var attempt = await _silent.TryCompleteAsync(
            target.Connection, target.Credential, consent.FirstPartyLoginUri, ct).ConfigureAwait(false);
        if (!attempt.Completed)
            return RequiresPerson(pending, state, consent, attempt.Failure);

        var refreshed = await _reader.GetStateAsync(target, pending.Connector.ApiId, pending.ConnectionId, ct)
            .ConfigureAwait(false);
        return refreshed.Error is null
            ? pending.Outcome(pending.HealthyStatus, refreshed.Status, null, null)
            : RequiresPerson(pending, refreshed, consent, refreshed.Error);
    }

    private static ConnectionCreateOutcome RequiresPerson(
        PendingConnection pending, ConnectionState state, ConnectionConsentInfo consent, string? message)
        => pending.Outcome(ConnectionOutcomeStatus.ConsentRequired, state.Status, consent.ConsentLink, message);
}
