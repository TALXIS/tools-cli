namespace TALXIS.CLI.Config.Model;

/// <summary>
/// Result of <see cref="Abstractions.IConfigurationResolver.ResolveAsync"/>.
/// <see cref="Profile"/> is null when the context was built ephemerally from
/// environment variables (no stored profile).
/// </summary>
public sealed record ResolvedProfileContext(
    Profile? Profile,
    Connection Connection,
    Credential Credential,
    ResolutionSource Source);

public enum ResolutionSource
{
    /// <summary>Profile came from <c>--profile</c> flag.</summary>
    CommandLine,
    /// <summary>Profile came from <c>TXC_PROFILE</c>.</summary>
    EnvironmentVariable,
    /// <summary>Profile came from workspace <c>.txc/workspace.json</c>.</summary>
    Workspace,
    /// <summary>Profile came from global active pointer.</summary>
    Global,
    /// <summary>Ephemeral context built from env vars (no stored profile).</summary>
    Ephemeral,
}
