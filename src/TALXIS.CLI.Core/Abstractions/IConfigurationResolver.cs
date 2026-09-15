using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Core.Abstractions;

/// <summary>
/// Resolves the (Profile, Connection, Credential) triple for a given invocation.
/// Precedence (highest first):
/// <list type="number">
/// <item>Explicit <paramref name="profileName"/> argument (command-line).</item>
/// <item><c>TXC_PROFILE</c> environment variable.</item>
/// <item>Workspace <c>.txc/workspace.json</c> (cwd-walk).</item>
/// <item>Global active-profile pointer in <c>config.json</c>.</item>
/// <item>Ephemeral context from environment variables (no stored profile).</item>
/// </list>
/// Throws <see cref="ConfigurationResolutionException"/> when none of the layers yield a context.
/// </summary>
public interface IConfigurationResolver
{
    Task<ResolvedProfileContext> ResolveAsync(string? profileName, CancellationToken ct);
}

#pragma warning disable RS0030 // Domain-specific exception type — inheriting from Exception is intentional
public sealed class ConfigurationResolutionException : Exception
{
    public ConfigurationResolutionException(string message) : base(message) { }
    public ConfigurationResolutionException(string message, Exception inner) : base(message, inner) { }
    public ConfigurationResolutionException(string message, ConfigurationResolutionFailureReason reason) : base(message)
        => Reason = reason;
    public ConfigurationResolutionException(string message, Exception inner, ConfigurationResolutionFailureReason reason) : base(message, inner)
        => Reason = reason;

    public ConfigurationResolutionFailureReason Reason { get; } = ConfigurationResolutionFailureReason.Unspecified;
}
#pragma warning restore RS0030

public enum ConfigurationResolutionFailureReason
{
    Unspecified = 0,
    NoProfile = 1,
}
