using System.Text.RegularExpressions;

namespace TALXIS.CLI.Logging;

/// <summary>
/// Redacts sensitive information from log messages before forwarding.
/// </summary>
public static partial class LogRedactionFilter
{
    private static readonly string HomePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string Redact(string message)
    {
        if (string.IsNullOrEmpty(message))
            return message;

        // Redact connection string values (AuthType=...;Password=...;)
        message = ConnectionStringPasswordRegex().Replace(message, "$1***REDACTED***$2");

        // Redact tokens/keys in query parameters (?token=xxx, &key=xxx)
        message = QueryParamSecretRegex().Replace(message, "$1***REDACTED***");

        // Replace home directory paths with ~
        if (!string.IsNullOrEmpty(HomePath))
        {
            message = message.Replace(HomePath, "~");
        }

        return message;
    }

    [GeneratedRegex(@"((?:Password|ClientSecret|Secret|Token)\s*=\s*)(.*?)(;|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ConnectionStringPasswordRegex();

    [GeneratedRegex(@"((?:token|key|secret|password|apikey|api_key)=)[^&\s]*", RegexOptions.IgnoreCase)]
    private static partial Regex QueryParamSecretRegex();
}
