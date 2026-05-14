namespace TALXIS.CLI.Core.Vault;

/// <summary>
/// Thrown when the OS credential vault (DPAPI / Keychain / libsecret) cannot be
/// initialised or verified. Surfaces a user-facing remedy string describing how
/// to unblock the situation (install <c>libsecret-1-0</c>, opt in to a plaintext
/// fallback, etc.).
/// </summary>
public sealed class VaultUnavailableException : Exception
{
    /// <summary>
    /// Canonical remedy message shown to the user when the vault fails to
    /// persist. The message covers all platforms so it is useful regardless
    /// of the OS the CLI is running on.
    /// </summary>
    public const string RemedyMessage =
        "OS credential vault is unavailable. " +
        "On Linux install `libsecret-1-0` and `gnome-keyring` (or run inside a desktop session with D-Bus). " +
        "On macOS set `TXC_TOKEN_CACHE_MODE=file` if Keychain is unavailable. " +
        "To opt in to a plaintext file fallback on any platform, set " +
        "`TXC_PLAINTEXT_FALLBACK=1`.";

    public VaultUnavailableException()
        : base(RemedyMessage) { }

    public VaultUnavailableException(Exception inner)
        : base(RemedyMessage, inner) { }

    public VaultUnavailableException(string message, Exception? inner = null)
        : base(message, inner) { }
}
