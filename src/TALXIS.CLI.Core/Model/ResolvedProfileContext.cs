namespace TALXIS.CLI.Core.Model;

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
    CommandLine = 0,
    /// <summary>Profile came from <c>TXC_PROFILE</c>.</summary>
    EnvironmentVariable = 1,
    /// <summary>Profile came from workspace <c>.txc/workspace.json</c>.</summary>
    Workspace = 2,
    /// <summary>Profile came from global active pointer.</summary>
    Global = 3,
    /// <summary>Ephemeral context built from env vars (no stored profile).</summary>
    Ephemeral = 4,
}
