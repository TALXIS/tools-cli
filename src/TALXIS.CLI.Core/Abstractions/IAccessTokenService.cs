using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Core.Abstractions;

/// <summary>
/// Provider-agnostic contract for acquiring bearer tokens scoped to a
/// particular resource URI. Each platform adapter (Dataverse, etc.)
/// implements this for its own identity stack; consumers like the Power
/// Platform control-plane catalog use it without coupling to a specific
/// provider's MSAL wiring.
/// </summary>
public interface IAccessTokenService
{
    /// <summary>
    /// Acquires a bearer token for the given <paramref name="resourceUri"/>
    /// using the identity described by the (Connection, Credential) pair.
    /// </summary>
    Task<string> AcquireForResourceAsync(
        Connection connection,
        Credential credential,
        Uri resourceUri,
        CancellationToken ct);
}
