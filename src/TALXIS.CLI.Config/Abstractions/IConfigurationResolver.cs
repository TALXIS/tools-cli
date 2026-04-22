using TALXIS.CLI.Config.Model;

namespace TALXIS.CLI.Config.Abstractions;

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

public sealed class ConfigurationResolutionException : Exception
{
    public ConfigurationResolutionException(string message) : base(message) { }
    public ConfigurationResolutionException(string message, Exception inner) : base(message, inner) { }
}
