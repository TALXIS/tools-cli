namespace TALXIS.CLI.Config.Vault;

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
    /// persist. Kept as a constant so tests can assert on it verbatim and it
    /// stays in sync with the README.
    /// </summary>
    public const string RemedyMessage =
        "OS credential vault is unavailable. On Linux install `libsecret-1-0` " +
        "and `gnome-keyring` (or run inside a desktop session with D-Bus). To " +
        "opt in to a plaintext file (chmod 600) fallback, re-run with " +
        "`--plaintext-fallback` or set `TXC_PLAINTEXT_FALLBACK=1`.";

    public VaultUnavailableException()
        : base(RemedyMessage) { }

    public VaultUnavailableException(Exception inner)
        : base(RemedyMessage, inner) { }

    public VaultUnavailableException(string message, Exception? inner = null)
        : base(message, inner) { }
}
