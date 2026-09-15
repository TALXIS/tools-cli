using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Core.Abstractions;

/// <summary>
/// Performs a device code sign-in against Entra. The user is presented with
/// a URL and a code to enter on another device (or browser tab), making this
/// suitable for browser-isolated environments like GitHub Codespaces, SSH
/// sessions, or containers without a desktop.
/// </summary>
/// <remarks>
/// Semantically equivalent to <see cref="IInteractiveLoginService"/> but
/// does not require a local browser or <c>http://localhost</c> redirect.
/// The resulting token is persisted identically to an interactive browser
/// credential (same MSAL token cache, same silent renewal path).
/// </remarks>
public interface IDeviceCodeLoginService
{
    /// <param name="tenantId">
    /// Optional Entra tenant id or domain. When <c>null</c>, the
    /// <c>organizations</c> endpoint is used and the tenant is inferred
    /// from whichever account the user picks.
    /// </param>
    /// <param name="cloud">Sovereign cloud for the authority URL.</param>
    Task<InteractiveLoginResult> LoginAsync(
        string? tenantId,
        CloudInstance cloud,
        CancellationToken ct);
}
