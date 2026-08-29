namespace TALXIS.CLI.Core.Headless;

/// <summary>
/// Auth failure that carries the target environment URL and renders the exact
/// <c>txc config profile create --url &lt;url&gt;</c> remedy the user must run manually.
/// </summary>
public sealed class EnvironmentAuthRequiredException : Exception
{
    public string EnvironmentUrl { get; }
    public string? HeadlessReason { get; }

#pragma warning disable RS0030 // domain exception - inheriting from Exception is intentional
    public EnvironmentAuthRequiredException(string environmentUrl, string? headlessReason = null, Exception? innerException = null)
        : base(BuildMessage(environmentUrl, headlessReason), innerException)
#pragma warning restore RS0030
    {
        EnvironmentUrl = environmentUrl;
        HeadlessReason = headlessReason;
    }

    public static string BuildMessage(string environmentUrl, string? headlessReason)
    {
        var url = NormalizeUrl(environmentUrl);
        var target = url ?? "the target environment";

        var lines = new List<string>
        {
            $"Action required - sign in to {target} before retrying:",
            url is not null
                ? $"  txc config profile create --url {url}"
                : "  txc config profile create --url <environment-url>",
            "Run that command manually in an interactive terminal (browser required), then retry.",
        };

        var why = "txc never signs in on its own: interactive sign-in needs a human in the loop, so agents and non-interactive runs are blocked from it.";
        if (!string.IsNullOrWhiteSpace(headlessReason)) why += $" This run is non-interactive ({headlessReason}).";
        lines.Add(why);

        return string.Join(Environment.NewLine, lines);
    }

    private static string? NormalizeUrl(string? environmentUrl)
    {
        if (string.IsNullOrWhiteSpace(environmentUrl)) return null;
        return Uri.TryCreate(environmentUrl, UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Authority) + "/"
            : environmentUrl.TrimEnd('/') + "/";
    }
}
