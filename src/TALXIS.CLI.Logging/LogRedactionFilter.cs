using System.Text.RegularExpressions;

namespace TALXIS.CLI.Logging;

/// <summary>
/// Redacts sensitive information from log messages before forwarding.
/// Applied by <c>McpLogForwarder</c> and the <c>JsonStderrLogger</c> so any
/// path that funnels exception text / structured log values into stderr is
/// sanitised.
/// </summary>
public static partial class LogRedactionFilter
{
    private const string RedactedMarker = "***REDACTED***";

    private static readonly string HomePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string? Redact(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return message;

        // Bearer <token> — common in HTTP traces.
        message = BearerTokenRegex().Replace(message, $"Bearer {RedactedMarker}");

        // Authorization: <anything> — Entire header value regardless of scheme.
        message = AuthorizationHeaderRegex().Replace(message, $"Authorization: {RedactedMarker}");

        // Bare JWTs (three dot-separated base64url segments). Placed after
        // Bearer/Authorization replacements so we also catch JWTs that leak
        // outside of headers (e.g. verbose SDK logs that print the raw token).
        message = JwtRegex().Replace(message, RedactedMarker);

        // Redact connection string values. Extended beyond the original
        // Password/ClientSecret/Secret/Token to also cover names the
        // ServiceClient / MSAL / Azure SDKs emit in diagnostics.
        message = ConnectionStringSecretRegex().Replace(message, "$1" + RedactedMarker + "$3");

        // Redact tokens/keys in query parameters (?token=xxx, &key=xxx)
        message = QueryParamSecretRegex().Replace(message, "$1" + RedactedMarker);

        // Replace home directory paths with ~
        if (!string.IsNullOrEmpty(HomePath))
        {
            message = message.Replace(HomePath, "~");
        }

        return message;
    }

    [GeneratedRegex(
        @"((?:Password|ClientSecret|ApplicationSecret|ApplicationPassword|Secret|Token|AccessToken|RefreshToken|IdToken|SasToken|ApiKey|Api_Key)\s*=\s*)([^;&?\s]*)(;|$)",
        RegexOptions.IgnoreCase)]
    private static partial Regex ConnectionStringSecretRegex();

    [GeneratedRegex(@"((?:token|key|secret|password|apikey|api_key|access_token|refresh_token|id_token)=)[^&\s]*", RegexOptions.IgnoreCase)]
    private static partial Regex QueryParamSecretRegex();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(@"Authorization\s*:\s*[^\r\n]+", RegexOptions.IgnoreCase)]
    private static partial Regex AuthorizationHeaderRegex();

    [GeneratedRegex(@"eyJ[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{4,}\.[A-Za-z0-9_\-]{4,}")]
    private static partial Regex JwtRegex();
}
