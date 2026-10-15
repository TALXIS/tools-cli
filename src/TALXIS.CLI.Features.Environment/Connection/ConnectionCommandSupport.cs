using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Features.Environment.Connection;

/// <summary>
/// The command that helps after a typed connection or connector error. The platform
/// words the problem; only the CLI knows its own grammar, so the hint lives here.
/// </summary>
internal static class ConnectionCommandSupport
{
    /// <summary>The next step for a typed error, or null when the message already says it all.</summary>
    internal static string? HintFor(Exception ex) => ex switch
    {
        ConnectionNotFoundException => "Run 'txc environment connection list' to see the connections you can use.",
        ConnectionAccessDeniedException => "Its owner can share it with 'txc environment connection permission add'.",
        AmbiguousConnectionException => "Pass --connector to choose one.",
        ConnectorNotFoundException => "Run 'txc environment connector list' to see what is available.",
        _ => null,
    };
}
